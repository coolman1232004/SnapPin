namespace SnapAnchor.Models;

internal sealed record RecognizedWord(
    string Text,
    int X,
    int Y,
    int Width,
    int Height,
    int LineIndex,
    float Confidence);

internal sealed record RecognitionResult(
    string Text,
    string BarcodeText,
    string BarcodeFormat,
    string ErrorMessage = "",
    IReadOnlyList<RecognizedWord>? Words = null)
{
    public bool HasContent => !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(BarcodeText);
    public IReadOnlyList<RecognizedWord> RecognizedWords => Words ?? Array.Empty<RecognizedWord>();
}
