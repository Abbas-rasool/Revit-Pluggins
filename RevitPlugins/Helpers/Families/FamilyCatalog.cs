using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;

namespace RevitPlugins.Families
{
    public enum DocumentKind
    {
        File,
        SourceFolder,
        Folder,
        Location,
        Link,
        SourceFile,
        HardCopyTag,
        Family,
        Note
    }

    /// <summary>Common surface for every entry in the family catalog.</summary>
    public interface IDocument
    {
        int FileId { get; }
        string FileName { get; }
        string DisplayName { get; }
        string Category { get; }
        DocumentKind Kind { get; }
        UIApplication App { get; set; }

        void Open();
    }

    /// <summary>
    /// Reads a JSON catalog describing the firm's Revit content library and exposes
    /// lookups by id or name. Only family entries carry an executable <see cref="IDocument.Open"/>;
    /// the other kinds are kept as inert records so the full catalog file still deserializes.
    /// </summary>
    public class FamilyCatalogService
    {
        private readonly List<IDocument> _documents;

        public FamilyCatalogService(string jsonPath)
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new DocumentConverter() }
            };

            var json = File.ReadAllText(jsonPath);

            _documents = JsonConvert.DeserializeObject<List<IDocument>>(json, settings)
                         ?? new List<IDocument>();
        }

        public IEnumerable<IDocument> GetAll() => _documents;
        public IDocument GetById(int id) => _documents.FirstOrDefault(d => d.FileId == id);
        public IDocument GetByName(string part) =>
            _documents.FirstOrDefault(d => d.FileName?.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// Resolves the concrete document type from the "Kind" discriminator in the JSON.
    /// Family entries become a <see cref="FamilyDocument"/>; anything else becomes an
    /// inert <see cref="GenericDocument"/> record (kept so the whole catalog still loads).
    /// </summary>
    public class DocumentConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(IDocument);

        public override object ReadJson(
            JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jo = Newtonsoft.Json.Linq.JObject.Load(reader);

            var kindToken = jo["Kind"]
                ?? throw new JsonSerializationException("Missing Kind property");

            var kind = kindToken.ToObject<DocumentKind>(serializer);

            IDocument doc = kind == DocumentKind.Family
                ? new FamilyDocument()
                : new GenericDocument();

            serializer.Populate(jo.CreateReader(), doc);
            return doc;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    /// <summary>Inert catalog record for non-family kinds (folders, links, notes, ...).</summary>
    public class GenericDocument : IDocument
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public DocumentKind Kind { get; set; }
        public UIApplication App { get; set; }

        public void Open() { /* No executable action for non-family entries. */ }
    }

    /// <summary>
    /// A family in the catalog. Mirrors the library .rfa to a local cache (prompting
    /// when the server copy is newer), then loads / activates it in the active document.
    /// </summary>
    public class FamilyDocument : IDocument
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public DocumentKind Kind { get; set; }

        public string SourcePath { get; set; }
        public string FileExtension { get; set; }
        public bool PlaceInteractively { get; set; } = true;

        // Injected by the command that opens the family.
        public UIApplication App { get; set; }

        private string GetFileName() => FileName + FileExtension;
        private string GetSourceFullPath() => Path.Combine(SourcePath, GetFileName());
        private string GetTargetFullPath() => LocalCache.GetFullPath(GetFileName());

        public string LocateFamily()
        {
            string source = GetSourceFullPath();
            string dest = GetTargetFullPath();
            var dir = Path.GetDirectoryName(dest);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(dest))
            {
                NetworkCopy(source, dest);
                return dest;
            }

            bool sourceIsNewer = false;
            var checkThread = new System.Threading.Thread(() =>
            {
                try
                {
                    var sourceInfo = new FileInfo(source);
                    var destInfo = new FileInfo(dest);
                    sourceIsNewer = sourceInfo.LastWriteTime > destInfo.LastWriteTime
                                 || sourceInfo.Length != destInfo.Length;
                }
                catch (Exception)
                {
                    sourceIsNewer = false;
                }
            });

            checkThread.IsBackground = true;
            checkThread.Start();
            checkThread.Join(TimeSpan.FromSeconds(10));

            if (!sourceIsNewer)
                return dest;

            var dialog = new TaskDialog("File Update Available");
            dialog.MainInstruction = "A newer version is available.";
            dialog.MainContent = "Would you like to download the latest version?\n\nClick 'No' to open the current local version.";
            dialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            dialog.DefaultButton = TaskDialogResult.Yes;

            if (dialog.Show() == TaskDialogResult.Yes)
                NetworkCopy(source, dest);

            return dest;
        }

        private void NetworkCopy(string source, string dest)
        {
            Exception copyEx = null;
            var t = new System.Threading.Thread(() =>
            {
                try { File.Copy(source, dest, true); }
                catch (Exception ex) { copyEx = ex; }
            });

            t.IsBackground = true;
            t.Start();

            bool finished = t.Join(TimeSpan.FromSeconds(60));

            if (!finished)
            {
                if (File.Exists(dest)) return;
                throw new TimeoutException("Timed out copying family from server (60s). Check your network connection.");
            }

            if (copyEx is IOException && File.Exists(dest))
                return;

            if (copyEx != null && !File.Exists(dest))
                throw copyEx;
        }

        public void Open()
        {
            try
            {
                var familyHelper = new FamilyHelper(App);

                var existing = familyHelper.FindLoadedFamily(FileName);

                Family family;
                if (existing != null)
                {
                    family = existing;
                }
                else
                {
                    string path = LocateFamily();
                    family = familyHelper.LoadFamilyIfNeeded(FileName, path);
                }

                var symbol = familyHelper.GetFirstSymbol(family);

                if (PlaceInteractively)
                    familyHelper.PlaceSymbol(symbol);
                else
                    familyHelper.ActivateSymbol(symbol);
            }
            catch (Exception ex)
            {
                var dialog = new TaskDialog("Could Not Open Family");
                dialog.MainInstruction = "Something went wrong — please try again.";
                dialog.MainContent = $"{DisplayName}\n\n{ex.Message}";
                dialog.CommonButtons = TaskDialogCommonButtons.Close;
                dialog.Show();
            }
        }
    }

    /// <summary>Local mirror of the network family library, under the user's Temp folder.</summary>
    public static class LocalCache
    {
        private const string RootFolderName = "00 Revit Plugins Cache";

        public static string GetRootFolder()
        {
            string tempRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");

            string path = Path.Combine(tempRoot, RootFolderName);

            Directory.CreateDirectory(path);

            return path;
        }

        public static string GetFullPath(string fileName) => Path.Combine(GetRootFolder(), fileName);
    }
}
