using Xunit;

namespace Chess.Api.Tests.Analysis;

/// <summary>A test that needs a POSIX shell to stand in for a UCI engine binary; skipped on Windows.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Needs /bin/sh to act as a UCI engine.";
        }
    }
}

/// <summary>
/// A shell script that behaves like a UCI engine, or like a broken one, in a temporary folder that is removed on dispose.
/// </summary>
internal sealed class FakeUciBinary : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("fake-uci-").FullName;

    private FakeUciBinary(string body, bool executable)
    {
        Path = System.IO.Path.Combine(_folder, "engine.sh");
        File.WriteAllText(Path, "#!/bin/sh\n" + body.Replace("{MARK}", System.IO.Path.Combine(_folder, "mark")));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path, executable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public string Path { get; }

    private const string Handshake = """
        uci) echo "id name Fake"; echo uciok ;;
        isready) echo readyok ;;
        """;

    /// <summary>Answers the handshake and every <c>go</c> with a score of +12 cp and the best move e2e4.</summary>
    public static FakeUciBinary Working() => new(
        $$"""
        while IFS= read -r line; do
          case "$line" in
            {{Handshake}}
            go*) echo "info depth 1 score cp 12 pv e2e4"; echo "bestmove e2e4 ponder e7e5" ;;
            quit) exit 0 ;;
          esac
        done
        """, executable: true);

    /// <summary>Never answers anything: stands for a hung binary or one that is not a UCI engine.</summary>
    public static FakeUciBinary Silent() => new("exec sleep 30\n", executable: true);

    /// <summary>Prints a line and exits: stands for a binary that is not a UCI engine.</summary>
    public static FakeUciBinary ExitsAtOnce() => new("echo \"not a chess engine\"\n", executable: true);

    /// <summary>Completes the handshake, then hangs on every <c>go</c>.</summary>
    public static FakeUciBinary HangsInEverySearch() => new(
        $$"""
        while IFS= read -r line; do
          case "$line" in
            {{Handshake}}
            go*) sleep 30 ;;
          esac
        done
        """, executable: true);

    /// <summary>Completes the handshake, hangs on the first <c>go</c> and answers every later one.</summary>
    public static FakeUciBinary HangsInTheFirstSearchOnly() => new(
        $$"""
        while IFS= read -r line; do
          case "$line" in
            {{Handshake}}
            go*)
              if [ -e "{MARK}" ]; then
                echo "info depth 1 score cp 12 pv e2e4"; echo "bestmove e2e4"
              else
                touch "{MARK}"; sleep 30
              fi ;;
          esac
        done
        """, executable: true);

    /// <summary>A file the system cannot run.</summary>
    public static FakeUciBinary NotExecutable() => new("exit 0\n", executable: false);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A killed process may still hold the folder for a moment; the OS temp cleaner removes it.
        }
    }
}
