using System.IO;
using System.Text;

namespace MDEdit.Services;

internal sealed class FileService
{
    public string? CurrentPath { get; private set; }

    public string LoadFile(string path)
    {
        CurrentPath = path;
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public void Save(string text)
    {
        if (CurrentPath is null)
            throw new InvalidOperationException("No file path is set.");
        File.WriteAllText(CurrentPath, text, Encoding.UTF8);
    }

    public void SaveAs(string path, string text)
    {
        CurrentPath = path;
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    public void Reset() => CurrentPath = null;
}
