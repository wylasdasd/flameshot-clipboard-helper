namespace FlameshotClipboardHelper.Core;

internal interface IClipboardWriter
{
    bool TryPushScreenshot(string filePath);
}
