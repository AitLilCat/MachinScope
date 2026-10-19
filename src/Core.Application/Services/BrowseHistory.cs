namespace Core.Application.Services;

public sealed class BrowseHistory(string initial, int capacity = 100)
{
    private readonly List<string> paths = [initial];
    private int position;
    public string Current => paths[position];
    public bool CanBack => position > 0;
    public bool CanForward => position + 1 < paths.Count;
    public void Navigate(string path)
    {
        if (Current == path) return;
        paths.RemoveRange(position + 1, paths.Count - position - 1); paths.Add(path);
        if (paths.Count > Math.Max(2, capacity)) paths.RemoveAt(0);
        position = paths.Count - 1;
    }
    public string Back() { if (CanBack) position--; return Current; }
    public string Forward() { if (CanForward) position++; return Current; }
}
