using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace YubiHsmCkaApplicationTest
{
    internal static class Program
    {
        // ------------------------------------------------------------------ //
        //  Configuration — edit these before running                          //
        // ------------------------------------------------------------------ //
        private static class Config
        {
            // Full path to the YubiHSM PKCS#11 shared library.
            public const string Pkcs11LibraryPath =
                "/usr/local/lib/pkcs11/yubihsm_pkcs11.so";

            // Slot index — usually 0 when only one connector is configured.
            public const ulong SlotId = 0;

            // Auth key ID (4 hex digits) concatenated with the password.
            // Default factory credentials: key 0x0001, password "password"
            public const string Pin = "0001password";

            // The CKA_APPLICATION string to round-trip.
            public const string ApplicationTag = "SmartcryptWrappedBinary";

            // CKA_LABEL used to locate the object.
            public const string ObjectLabel = "Custom-CKA_APPLICATION-Opq-Obj";

            // Payload stored as CKA_VALUE.
            public const string PayloadText = "Custom CKA_APPLICATION Opq Obj";
        }

        // ------------------------------------------------------------------ //
        //  Entry point                                                         //
        // ------------------------------------------------------------------ //
        private static int Main()
        {
            Console.WriteLine("=== YubiHSM CKA_APPLICATION Round-Trip Test ===");
            Console.WriteLine();

            try
            {
                Run();
                Console.WriteLine();
                Console.WriteLine("*** ALL CHECKS PASSED ***");
                return 0;
            }
            catch (TestFailureException ex)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"*** TEST FAILED: {ex.Message} ***");
                Console.ResetColor();
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"*** UNEXPECTED ERROR: {ex} ***");
                Console.ResetColor();
                return 2;
            }
        }

        // ------------------------------------------------------------------ //
        //  Main test logic                                                     //
        // ------------------------------------------------------------------ //
        private static void Run()
        {
            var factories = new Pkcs11InteropFactories();

            using IPkcs11Library lib = factories.Pkcs11LibraryFactory
                .LoadPkcs11Library(
                    factories,
                    Config.Pkcs11LibraryPath,
                    AppType.SingleThreaded);

            Step("Library loaded", Config.Pkcs11LibraryPath);

            // Locate slot
            ISlot slot = FindSlot(lib, Config.SlotId);
            Step("Slot found", $"slot id = {slot.SlotId}");

            using ISession session = slot.OpenSession(SessionType.ReadWrite);
            session.Login(CKU.CKU_USER, Config.Pin);
            Step("Session opened and logged in");

            // Clean up any leftover object from a previous run
            DeleteExistingTestObjects(session, factories);

            // ── Phase 1: Create ─────────────────────────────────────────── //
            IObjectHandle createdHandle = CreateDataObject(session, factories);
            Step("CKO_DATA object created",
                 $"handle = 0x{createdHandle.ObjectId:X}");

            // ── Phase 2: Find ────────────────────────────────────────────── //
            IObjectHandle foundHandle = FindDataObject(session, factories);
            Step("Object located by label",
                 $"handle = 0x{foundHandle.ObjectId:X}");

            // ── Phase 3: Verify ──────────────────────────────────────────── //
            ReadAndVerifyAttributes(session, foundHandle, factories);

            // Cleanup
            session.DestroyObject(foundHandle);
            Step("Test object deleted from device");

            session.Logout();
        }

        // ------------------------------------------------------------------ //
        //  Phase 1 — Create                                                   //
        // ------------------------------------------------------------------ //
        private static IObjectHandle CreateDataObject(
            ISession session,
            Pkcs11InteropFactories factories)
        {
            Console.WriteLine();
            Console.WriteLine(
                "── Phase 1: Creating CKO_DATA object ──────────────────");

            byte[] labelBytes   = Encoding.UTF8.GetBytes(Config.ObjectLabel);
            byte[] appBytes     = Encoding.UTF8.GetBytes(Config.exit);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(Config.PayloadText);

            var template = new List<IObjectAttribute>
            {
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_CLASS, (ulong)CKO.CKO_DATA),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_TOKEN, true),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_PRIVATE, false),
                // factories.ObjectAttributeFactory.Create(
                //    CKA.CKA_MODIFIABLE, false),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_DESTROYABLE, true),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_LABEL, labelBytes),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_APPLICATION, appBytes),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_VALUE, payloadBytes),
            };

            Console.WriteLine("  Template:");
            Console.WriteLine($"    CKA_CLASS       = CKO_DATA");
            Console.WriteLine($"    CKA_TOKEN       = true");
            Console.WriteLine($"    CKA_LABEL       = \"{Config.ObjectLabel}\"");
            Console.WriteLine($"    CKA_APPLICATION = \"{Config.ApplicationTag}\"");
            Console.WriteLine($"    CKA_VALUE       = \"{Config.PayloadText}\"");

            return session.CreateObject(template);
        }

        // ------------------------------------------------------------------ //
        //  Phase 2 — Find                                                     //
        // ------------------------------------------------------------------ //
        private static IObjectHandle FindDataObject(
            ISession session,
            Pkcs11InteropFactories factories)
        {
            Console.WriteLine();
            Console.WriteLine(
                "── Phase 2: Finding object by CKA_LABEL ───────────────");

            byte[] labelBytes = Encoding.UTF8.GetBytes(Config.ObjectLabel);

            var searchTemplate = new List<IObjectAttribute>
            {
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_CLASS, (ulong)CKO.CKO_DATA),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_TOKEN, true),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_LABEL, labelBytes),
            };

            IObjectHandle found =
                session.FindAllObjects(searchTemplate).FirstOrDefault()
                ?? throw new TestFailureException(
                    "FindObjects returned no results — " +
                    "object was not stored on the device.");

            return found;
        }

        // ------------------------------------------------------------------ //
        //  Phase 3 — Verify                                                   //
        // ------------------------------------------------------------------ //
        private static void ReadAndVerifyAttributes(
            ISession session,
            IObjectHandle handle,
            Pkcs11InteropFactories factories)
        {
            Console.WriteLine();
            Console.WriteLine(
                "── Phase 3: Reading and verifying attributes ───────────");

            var requestedTypes = new List<ulong>
            {
                (ulong)CKA.CKA_CLASS,
                (ulong)CKA.CKA_TOKEN,
                (ulong)CKA.CKA_LABEL,
                (ulong)CKA.CKA_APPLICATION,
                (ulong)CKA.CKA_VALUE,
            };

            List<IObjectAttribute> attrs =
                session.GetAttributeValue(handle, requestedTypes);

            Console.WriteLine("  Retrieved attributes:");
          //  foreach (IObjectAttribute a in attrs)
          //  {
          //      string typeName = Enum.IsDefined(typeof(CKA), a.Type)
          //          ? ((CKA)a.Type).ToString()
          //          : $"0x{a.Type:X8}";
          //      Console.WriteLine($"    {typeName,-25} = {DescribeAttribute(a)}");
          //  }
            foreach (IObjectAttribute a in attrs)
            {
               // Check if the ulong fits into a uint, then cast it to match the CKA enum's underlying type
               string typeName = (a.Type <= uint.MaxValue && Enum.IsDefined(typeof(CKA), (uint)a.Type))
               ? ((CKA)(uint)a.Type).ToString()
               : $"0x{a.Type:X8}";
        
               Console.WriteLine($"    {typeName,-25} = {DescribeAttribute(a)}");
            }

            Console.WriteLine();

            // CKA_CLASS
            ulong cls = GetUlongAttr(attrs, CKA.CKA_CLASS);
            if (cls != (ulong)CKO.CKO_DATA)
                throw new TestFailureException(
                    $"CKA_CLASS: expected CKO_DATA (0) but got 0x{cls:X}");
            Console.WriteLine("  ✓ CKA_CLASS       = CKO_DATA");

            // CKA_TOKEN
            bool token = GetBoolAttr(attrs, CKA.CKA_TOKEN);
            if (!token)
                throw new TestFailureException(
                    "CKA_TOKEN is false — object is not persistent.");
            Console.WriteLine("  ✓ CKA_TOKEN       = true");

            // CKA_LABEL
            string label = GetStringAttr(attrs, CKA.CKA_LABEL);
            if (label != Config.ObjectLabel)
                throw new TestFailureException(
                    $"CKA_LABEL: expected [{Config.ObjectLabel}] " +
                    $"but got [{label}]");
            Console.WriteLine($"  ✓ CKA_LABEL       = \"{label}\"");

            // CKA_APPLICATION — the key assertion
            string application = GetStringAttr(attrs, CKA.CKA_APPLICATION);
            if (application != Config.ApplicationTag)
                throw new TestFailureException(
                    $"CKA_APPLICATION: expected [{Config.ApplicationTag}] " +
                    $"but got [{application}]");
            Console.WriteLine($"  ✓ CKA_APPLICATION = \"{application}\"");

            // CKA_VALUE
            string value = GetStringAttr(attrs, CKA.CKA_VALUE);
            if (value != Config.PayloadText)
                throw new TestFailureException(
                    $"CKA_VALUE: expected [{Config.PayloadText}] " +
                    $"but got [{value}]");
            Console.WriteLine($"  ✓ CKA_VALUE       = \"{value}\"");
        }

        // ------------------------------------------------------------------ //
        //  Helpers — attribute reading                                         //
        // ------------------------------------------------------------------ //
        private static IObjectAttribute GetAttr(
            IEnumerable<IObjectAttribute> attrs, CKA type)
        {
            IObjectAttribute a = attrs.FirstOrDefault(
                x => x.Type == (ulong)type);
            if (a == null)
                throw new TestFailureException(
                    $"Attribute {type} was not returned by " +
                    "GetAttributeValue.");
            return a;
        }

        private static ulong GetUlongAttr(
            IEnumerable<IObjectAttribute> attrs, CKA type)
            => GetAttr(attrs, type).GetValueAsUlong();

        private static bool GetBoolAttr(
            IEnumerable<IObjectAttribute> attrs, CKA type)
            => GetAttr(attrs, type).GetValueAsBool();

        private static string GetStringAttr(
            IEnumerable<IObjectAttribute> attrs, CKA type)
        {
            byte[] raw = GetAttr(attrs, type).GetValueAsByteArray();
            if (raw == null || raw.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(raw);
        }

        // ------------------------------------------------------------------ //
        //  Helpers — slot                                                      //
        // ------------------------------------------------------------------ //
        private static ISlot FindSlot(IPkcs11Library lib, ulong slotId)
        {
            List<ISlot> slots = lib.GetSlotList(SlotsType.WithTokenPresent);

            if (slots.Count == 0)
                throw new TestFailureException(
                    "No slots with a token present. " +
                    "Is yubihsm-connector running?");

            ISlot slot = slots.FirstOrDefault(s => s.SlotId == slotId);
            if (slot == null)
                throw new TestFailureException(
                    $"Slot {slotId} not found. Available: " +
                    string.Join(", ", slots.Select(s => s.SlotId)));

            return slot;
        }

        // ------------------------------------------------------------------ //
        //  Helpers — cleanup                                                   //
        // ------------------------------------------------------------------ //
        private static void DeleteExistingTestObjects(
            ISession session,
            Pkcs11InteropFactories factories)
        {
            byte[] labelBytes = Encoding.UTF8.GetBytes(Config.ObjectLabel);

            var template = new List<IObjectAttribute>
            {
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_CLASS, (ulong)CKO.CKO_DATA),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_TOKEN, true),
                factories.ObjectAttributeFactory.Create(
                    CKA.CKA_LABEL, labelBytes),
            };

            List<IObjectHandle> existing = session.FindAllObjects(template);
            if (existing.Count == 0) return;

            Console.WriteLine(
                $"  (Removing {existing.Count} leftover test object(s) " +
                "from a previous run.)");

            foreach (IObjectHandle h in existing)
                session.DestroyObject(h);
        }

        // ------------------------------------------------------------------ //
        //  Helpers — logging                                                   //
        // ------------------------------------------------------------------ //
        private static void Step(string msg, string detail = null)
        {
            Console.Write($"[OK] {msg}");
            if (detail != null) Console.Write($" — {detail}");
            Console.WriteLine();
        }

        private static string DescribeAttribute(IObjectAttribute a)
        {
            try { return a.GetValueAsBool().ToString(); } catch { }
            try
            {
                ulong u = a.GetValueAsUlong();
                return $"0x{u:X} ({u})";
            }
            catch { }
            try
            {
                byte[] raw = a.GetValueAsByteArray();
                if (raw == null || raw.Length == 0) return "<empty>";
                string s = Encoding.UTF8.GetString(raw);
                bool printable = s.All(c => !char.IsControl(c));
                return printable ? $"\"{s}\"" : BitConverter.ToString(raw);
            }
            catch { }
            return "<unreadable>";
        }
    }

    // ---------------------------------------------------------------------- //
    //  Custom exception                                                        //
    // ---------------------------------------------------------------------- //
    internal sealed class TestFailureException : Exception
    {
        public TestFailureException(string message) : base(message) { }
    }
}
