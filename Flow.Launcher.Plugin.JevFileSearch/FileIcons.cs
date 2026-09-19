using System;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Maps a result to one of the bundled type icons, so a row shows what kind of thing
    /// it is instead of repeating the plugin icon. Keep the names in sync with
    /// images/filetypes.
    /// </summary>
    public static class FileIcons
    {
        public const string Default = "images/filetypes/file.png";
        public const string Folder = "images/filetypes/folder.png";
        public const string App = "images/filetypes/app.png";
        public const string Toggle = "images/filetypes/toggle.png";
        public const string Drive = "images/filetypes/drive.png";
        public const string Pdf = "images/filetypes/pdf.png";
        public const string Image = "images/filetypes/image.png";
        public const string Video = "images/filetypes/video.png";
        public const string Audio = "images/filetypes/audio.png";
        public const string Archive = "images/filetypes/archive.png";
        public const string Code = "images/filetypes/code.png";
        public const string Text = "images/filetypes/text.png";
        public const string Sheet = "images/filetypes/sheet.png";
        public const string Doc = "images/filetypes/doc.png";

        private static readonly Dictionary<string, string> ByExtension =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = Pdf,
            ["png"] = Image, ["jpg"] = Image, ["jpeg"] = Image, ["gif"] = Image, ["heic"] = Image,
            ["webp"] = Image, ["bmp"] = Image, ["tif"] = Image, ["tiff"] = Image, ["svg"] = Image,
            ["mp4"] = Video, ["mov"] = Video, ["m4v"] = Video, ["mkv"] = Video, ["avi"] = Video,
            ["wmv"] = Video, ["webm"] = Video,
            ["mp3"] = Audio, ["wav"] = Audio, ["flac"] = Audio, ["m4a"] = Audio, ["aac"] = Audio,
            ["ogg"] = Audio,
            ["zip"] = Archive, ["7z"] = Archive, ["rar"] = Archive, ["tar"] = Archive, ["gz"] = Archive,
            ["exe"] = App, ["msi"] = App, ["bat"] = Code, ["cmd"] = Code, ["ps1"] = Code,
            ["cs"] = Code, ["ts"] = Code, ["js"] = Code, ["py"] = Code, ["java"] = Code, ["go"] = Code,
            ["rs"] = Code, ["cpp"] = Code, ["c"] = Code, ["h"] = Code, ["json"] = Code, ["yml"] = Code,
            ["yaml"] = Code, ["xml"] = Code, ["html"] = Code, ["css"] = Code, ["sh"] = Code,
            ["md"] = Text, ["txt"] = Text, ["rtf"] = Text, ["log"] = Text,
            ["xlsx"] = Sheet, ["xls"] = Sheet, ["csv"] = Sheet, ["ods"] = Sheet,
            ["doc"] = Doc, ["docx"] = Doc, ["pptx"] = Doc, ["ppt"] = Doc, ["odt"] = Doc,
        };

        public static string ForExtension(string extension)
        {
            string ext = (extension ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0)
                return Default;
            return ByExtension.TryGetValue(ext, out var icon) ? icon : Default;
        }
    }
}
