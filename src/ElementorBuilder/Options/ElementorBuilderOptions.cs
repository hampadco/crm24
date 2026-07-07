namespace ElementorBuilder.Options;

public class ElementorBuilderOptions
{
    public const string SectionName = "ElementorBuilder";

    public string ContentFieldId { get; set; } = "Content";

    public string UploadUrl { get; set; } = "/Elementor/Media/Upload";

    public string DeleteUrl { get; set; } = "/Elementor/Media/Delete";

    public string UploadFolder { get; set; } = "uploads/elementor";

    /// <summary>
    /// Extra upload folders whose files may be deleted when unreferenced (e.g. legacy paths).
    /// </summary>
    public string[] AdditionalManagedUploadFolders { get; set; } = [];

    public string DraftStorageKey { get; set; } = "elementor-content-draft";

    public string CssPath { get; set; } = "/_content/ElementorBuilder/css/elementor-builder.css";

    public string ContentCssPath { get; set; } = "/_content/ElementorBuilder/css/elementor-content.css";

    public string JsPath { get; set; } = "/_content/ElementorBuilder/js/elementor-builder.js";

    public string FontAwesomePath { get; set; } = "/lib/fontawesome/6.4.0/css/all.min.css";

    public int MaxUploadSizeMb { get; set; } = 5;

    public string[] AllowedExtensions { get; set; } =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".mp3", ".mpeg", ".wav", ".ogg", ".m4a", ".aac", ".webm"
    ];

    public string[] AllowedImageExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public string[] AllowedAudioExtensions { get; set; } = [".mp3", ".mpeg", ".wav", ".ogg", ".m4a", ".aac", ".webm"];
}
