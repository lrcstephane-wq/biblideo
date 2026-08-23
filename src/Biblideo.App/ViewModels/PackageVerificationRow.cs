namespace Biblideo.App.ViewModels;

public sealed record PackageVerificationRow(
    string Library,
    int ComponentCount,
    int FileCount,
    string Status);
