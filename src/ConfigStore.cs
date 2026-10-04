using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OmenLiteControl
{
    internal static class ConfigStore
    {
        static XElement document;
        static string PathName
        {
            get {
                return UserData.File("config.xml");
            }
        }
        static readonly string[] LegacyFiles = { "language.txt", "window-settings.txt",
                                                 "mode-hotkey.txt", "keyboard-presets.txt" };
        static XElement Data
        {
            get {
                return document ?? (document = Load());
            }
        }

        static XElement Load()
        {
            if (File.Exists(PathName))
            {
                using (var reader = XmlReader.Create(
                           PathName, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                                                             XmlResolver = null }))
                {
                    var root = XElement.Load(reader);
                    if (root.Name != "config" || (string)root.Attribute("version") != "1")
                        throw new InvalidDataException("Unsupported config.xml format.");
                    return root;
                }
            }
            var result = new XElement("config", new XAttribute("version", "1"));
            result.Add(
                new XElement("language", Legacy("language.txt").Trim() == "en" ? "en" : "zh"));
            result.Add(
                new XElement("minimizeToTray", Legacy("window-settings.txt").Trim() == "tray=1"));
            string[] hotkey =
                Legacy("mode-hotkey.txt")
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int key = 0;
            bool valid = hotkey.Length == 2 && Int32.TryParse(hotkey[1], out key) &&
                         HotkeySettings.IsValid((System.Windows.Forms.Keys)key);
            result.Add(new XElement("hotkey", new XAttribute("enabled", valid && hotkey[0] == "1"),
                                    new XAttribute("key", valid ? key : 0)));
            result.Add(ParsePresets(
                Legacy("keyboard-presets.txt")
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
            SaveDocument(result);
            // Only migrate files alongside this application; never read the user-profile directory.
            foreach (string name in LegacyFiles)
            {
                string path = UserData.File(name);
                if (File.Exists(path))
                    File.Delete(path);
            }
            return result;
        }

        static string Legacy(string name)
        {
            string path = UserData.File(name);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }

        static void SaveDocument(XElement root)
        {
            string path = PathName, temp = path + ".tmp";
            using (var writer = XmlWriter.Create(temp, new XmlWriterSettings {
                Indent = true, Encoding = new UTF8Encoding(false)
            })) root.Save(writer);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        static void Update(Action<XElement> update)
        {
            var next = new XElement(Data);
            update(next);
            SaveDocument(next);
            document = next;
        }

        internal static string Get(string name, string fallback)
        {
            return (string)Data.Element(name) ?? fallback;
        }

        internal static void Set(string name, string value)
        {
            Update(root => root.SetElementValue(name, value));
        }

        internal static HotkeySettings GetHotkey()
        {
            var settings = new HotkeySettings();
            var element = Data.Element("hotkey");
            int key;
            if (element != null && Int32.TryParse((string)element.Attribute("key"), out key) &&
                HotkeySettings.IsValid((System.Windows.Forms.Keys)key))
            {
                settings.Key = (System.Windows.Forms.Keys)key;
                settings.Enabled = (string)element.Attribute("enabled") == "true";
            }
            return settings;
        }

        internal static void SetHotkey(HotkeySettings settings)
        {
            Update(root =>
                   {
                       root.Elements("hotkey").Remove();
                       root.Add(new XElement("hotkey", new XAttribute("enabled", settings.Enabled),
                                             new XAttribute("key", (int)settings.Key)));
                   });
        }

        static XElement ParsePresets(IEnumerable<string> lines)
        {
            string[] stored = lines.ToArray();
            var lighting = new XElement(
                "lighting", new XAttribute("enabled", stored.Contains("# lighting-enabled")),
                new XAttribute("initialized", stored.Contains("# presets-v2")));
            foreach (string line in stored)
            {
                string[] parts = line.Split('|');
                if (parts.Length == 3 && parts[0] == "# mode")
                    lighting.Add(new XElement("mode", new XAttribute("id", parts[1]),
                                              new XAttribute("preset", parts[2])));
                else if (parts.Length == 9)
                {
                    var preset = new XElement("preset", new XAttribute("name", parts[0]));
                    for (int i = 0; i < 4; i++)
                        preset.Add(new XElement("zone", new XAttribute("rgb", parts[1 + i * 2]),
                                                new XAttribute("brightness", parts[2 + i * 2])));
                    lighting.Add(preset);
                }
            }
            return lighting;
        }

        internal static string[] ReadPresets()
        {
            var lighting = Data.Element("lighting");
            var lines = new List<string>();
            if (lighting == null)
                return lines.ToArray();
            if ((string)lighting.Attribute("initialized") == "true")
                lines.Add("# presets-v2");
            if ((string)lighting.Attribute("enabled") == "true")
                lines.Add("# lighting-enabled");
            foreach (var mode in lighting.Elements("mode"))
                lines.Add("# mode|" + (string)mode.Attribute("id") + "|" +
                          (string)mode.Attribute("preset"));
            foreach (var preset in lighting.Elements("preset"))
                lines.Add((string)preset.Attribute("name") + "|" +
                          String.Join("|", preset.Elements("zone").SelectMany(
                                               z => new[] { (string)z.Attribute("rgb"),
                                                            (string)z.Attribute("brightness") })));
            return lines.ToArray();
        }

        internal static void SetPresets(IEnumerable<string> lines)
        {
            var lighting = ParsePresets(lines);
            Update(root =>
                   {
                       root.Elements("lighting").Remove();
                       root.Add(lighting);
                   });
        }
    }

    internal sealed class HotkeyComboBox : System.Windows.Forms.ComboBox
    {
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x020A || message.Msg == 0x020E)
                return;
            base.WndProc(ref message);
        }
    }
}
