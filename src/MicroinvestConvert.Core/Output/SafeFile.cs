using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.Core.Output;

/// <summary>A file is there whole, or as it was.</summary>
internal static class SafeFile
{
    /// <summary>Writes next to the target and swaps it in, so a failure never leaves half a file.</summary>
    /// <exception cref="ConvertException">It cannot be written – in words for the user.</exception>
    public static void Write(string path, byte[] content)
    {
        string temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw new ConvertException(
                "Файлът не може да бъде записан. Ако е отворен в Excel или в Делта Pro, затворете го и опитайте отново.\n\n" +
                ex.Message, ex);
        }
    }
}
