namespace Jellyfin.Plugin.MindTheGaps.Services.Images;

/// <summary>
/// What a trim of the image cache found and did.
/// </summary>
/// <param name="Files">The number of images the folder held.</param>
/// <param name="BytesBefore">The bytes it held.</param>
/// <param name="Deleted">The number of images deleted.</param>
/// <param name="BytesAfter">The bytes it holds now.</param>
internal sealed record TrimResult(int Files, long BytesBefore, int Deleted, long BytesAfter);
