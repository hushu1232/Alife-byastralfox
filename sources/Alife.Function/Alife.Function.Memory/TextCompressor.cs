using System.Threading.Tasks;

public abstract class TextCompressor
{
    public abstract Task<string?> Compress(string text);
}
