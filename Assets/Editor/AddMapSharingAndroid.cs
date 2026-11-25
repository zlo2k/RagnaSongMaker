using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEngine;

namespace RagnarockEditor.EditorTools
{
    public class AddMapSharingAndroid : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string main = Path.Combine(path, "src", "main");
            string manifest = Path.Combine(main, "AndroidManifest.xml");
            var document = XDocument.Load(manifest);
            XNamespace android = "http://schemas.android.com/apk/res/android";
            const string provider = "org.ragnasongmaker.share.MapProvider";
            var app = document.Root.Element("application");
            if (!app.Elements("provider").Any(p => (string)p.Attribute(android + "name") == provider))
            {
                app.Add(new XElement("provider",
                    new XAttribute(android + "name", provider),
                    new XAttribute(android + "authorities", "${applicationId}.maps"),
                    new XAttribute(android + "exported", "false"),
                    new XAttribute(android + "grantUriPermissions", "true"),
                    new XElement("meta-data",
                        new XAttribute(android + "name", "android.support.FILE_PROVIDER_PATHS"),
                        new XAttribute(android + "resource", "@xml/ragna_map_paths"))));
                document.Save(manifest);
            }

            string xml = Directory.CreateDirectory(Path.Combine(main, "res", "xml")).FullName;
            new XDocument(new XElement("paths", new XElement("cache-path",
                new XAttribute("name", "maps"), new XAttribute("path", "shared-maps/"))))
                .Save(Path.Combine(xml, "ragna_map_paths.xml"));
            string java = Directory.CreateDirectory(Path.Combine(main, "java", "org", "ragnasongmaker", "share")).FullName;
            File.Copy(Path.Combine(Application.dataPath, "Editor", "MapSharing~", "MapProvider.java"),
                Path.Combine(java, "MapProvider.java"), true);

            string gradle = Path.Combine(path, "build.gradle");
            const string dependency = "androidx.core:core:1.13.1";
            string script = File.ReadAllText(gradle);
            if (!script.Contains(dependency))
                File.AppendAllText(gradle, "\ndependencies { implementation '" + dependency + "' }\n");

            // Keep the legacy jdk7/jdk8 modules aligned with the stdlib used by AndroidX.
            const string kotlinBom = "org.jetbrains.kotlin:kotlin-bom:1.8.22";
            if (!script.Contains(kotlinBom))
                File.AppendAllText(gradle, "\ndependencies { implementation platform('" + kotlinBom + "') }\n");

            string properties = Path.Combine(Directory.GetParent(path).FullName, "gradle.properties");
            if (!File.Exists(properties) || !File.ReadAllLines(properties).Any(l => l.Trim() == "android.useAndroidX=true"))
                File.AppendAllText(properties, "\nandroid.useAndroidX=true\n");
        }
    }
}
