using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace RepoCheatMenu;

// Compiles and runs a user-typed C# script against ScriptGlobals (the public surface a script
// can see - see that file for why it's a separate class instead of exposing this mod's internals
// directly). Roslyn scripting (not CodeDom) because CodeDom's actual C#-compiling implementation
// never shipped for .NET Standard/Core - only Roslyn does real C# compilation on this TFM, which
// is why this needs its own NuGet package with CopyLocalLockFileAssemblies (see the .csproj
// comment) rather than something already available for free.
internal static class ScriptEngine
{
    private static ScriptOptions _options;

    private static ScriptOptions Options()
    {
        if (_options != null) return _options;

        // References resolved against whatever's ACTUALLY loaded in this game process right now
        // (typeof(X).Assembly), not the compile-time NuGet reference stubs this mod itself built
        // against - those two can be different DLLs on disk. A script that reaches for a
        // genuinely private field still throws FieldAccessException at runtime either way (the
        // CLR enforces that against the real loaded assembly, regardless of which reference
        // Roslyn compiled the script against), same as GameReflect.cs already documents for our
        // own mod code - scripts should use GetField/SetField/Call from ScriptGlobals for those.
        //
        // NOT using ScriptOptions.WithReferences(params Assembly[]) - confirmed the actual crash
        // this was reported to throw: "NotSupportedException: Can't create a metadata reference
        // to an assembly without location". BepInEx loads a plugin's own DLL via
        // Assembly.Load(byte[]) rather than from its file path (so the file isn't left locked
        // open while the game runs, letting it be replaced/updated) - Assembly.Location comes
        // back empty for an assembly loaded that way, and the Assembly-array overload needs a
        // real Location for every single one it's given, with no fallback. RepoCheatMenu.dll
        // itself is exactly that case. Building each MetadataReference by hand via
        // ToMetadataReference below sidesteps it, by locating the real file on disk ourselves
        // when Location is empty instead of trusting it to already be there.
        MetadataReference[] references = new[]
        {
            typeof(object).Assembly,                       // mscorlib/System.Private.CoreLib
            typeof(UnityEngine.Object).Assembly,            // UnityEngine.CoreModule
            typeof(UnityEngine.GameObject).Assembly,
            typeof(PlayerAvatar).Assembly,                  // Assembly-CSharp (the real one, loaded by the game)
            typeof(Photon.Pun.PhotonNetwork).Assembly,      // PhotonUnityNetworking.dll
            typeof(Photon.Realtime.RoomOptions).Assembly,   // PhotonRealtime.dll
            typeof(HarmonyLib.Harmony).Assembly,            // 0Harmony - inspect which patches actually applied
            typeof(ScriptGlobals).Assembly,                 // this mod itself - the Location-less one
        }.Select(ToMetadataReference).Where(r => r != null).ToArray();

        _options = ScriptOptions.Default
            .WithReferences(references)
            .WithImports("System", "System.Collections.Generic", "System.Threading.Tasks",
                "UnityEngine", "Photon.Pun", "Photon.Realtime", "HarmonyLib", "RepoCheatMenu");

        return _options;
    }

    private static MetadataReference ToMetadataReference(Assembly assembly)
    {
        string location = assembly.Location;
        if (string.IsNullOrEmpty(location))
            location = FindAssemblyFile(assembly.GetName().Name);

        if (string.IsNullOrEmpty(location))
        {
            Plugin.Log.LogWarning($"ScriptEngine: could not find a file on disk for " +
                $"{assembly.GetName().Name} - scripts won't be able to reference its types.");
            return null;
        }
        return MetadataReference.CreateFromFile(location);
    }

    // Assembly.Location comes back empty specifically for assemblies BepInEx loaded from a byte
    // array (see the comment above) - the real file is still sitting on disk right where it was
    // deployed, just not open as the loaded Assembly object's own source, so find it by simple
    // name instead. PluginPath covers this mod's own DLL; ManagedPath (REPO_Data/Managed) covers
    // the same fallback for any game/Photon assembly that ever turns out to need it too, for the
    // same reason, without hard-coding which one specifically.
    private static string FindAssemblyFile(string simpleName)
    {
        string fileName = simpleName + ".dll";
        foreach (string root in new[] { Paths.PluginPath, Paths.ManagedPath })
        {
            try
            {
                string found = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
            catch
            {
                // Inaccessible/missing directory - try the next root rather than failing the
                // whole reference-resolution pass over one bad path.
            }
        }
        return null;
    }

    public readonly struct Result
    {
        public readonly bool Success;
        public readonly string Message; // error text on failure, string.Empty on success
        public readonly string ReturnValue; // ToString() of the script's trailing expression, if any - null otherwise

        public Result(bool success, string message, string returnValue = null)
        {
            Success = success;
            Message = message;
            ReturnValue = returnValue;
        }

        public static readonly Result Ok = new(true, string.Empty);
        public static Result Fail(string message) => new(false, message);
    }

    // Compile-only - used to validate a module's source when it's saved, so a typo gets caught
    // and reported right there in the editor instead of silently doing nothing the next time
    // someone presses Run.
    public static Result CheckSyntax(string code)
    {
        try
        {
            Script<object> script = CSharpScript.Create(code, Options(), typeof(ScriptGlobals));
            List<string> errors = new();
            foreach (Diagnostic d in script.Compile())
                if (d.Severity == DiagnosticSeverity.Error)
                    errors.Add(d.ToString());

            return errors.Count == 0 ? Result.Ok : Result.Fail(string.Join("\n", errors));
        }
        catch (Exception ex)
        {
            return Result.Fail(ex.Message);
        }
    }

    // Deliberately NOT blocking (no .GetAwaiter().GetResult() here) - now that scripts can reach
    // Photon/Steam (HostLobby, CreateRoom, ...), a script legitimately needs to sit across
    // several real frames waiting for those async operations to complete, e.g.
    // "while (!Steam.currentLobby.Id.IsValid) await Task.Delay(50);". Blocking the calling
    // thread (the button click, which runs on Unity's main thread) to wait for that would be a
    // classic sync-over-async deadlock: Steamworks' own callback pump and Unity's
    // SynchronizationContext (which is what actually resumes a script after "await Task.Delay")
    // both need the main thread to keep running its normal frame loop, which a blocking wait
    // right there would prevent forever. fireAndReport instead starts the Task and returns
    // immediately, letting the game keep running; onComplete fires later, back on the main
    // thread (Unity installs a SynchronizationContext on it, which is what an "await" inside an
    // async method - this one - resumes on by default) once the script actually finishes.
    //
    // This does NOT rescue a script that busy-loops instead of awaiting - "while (!ready) {}"
    // with no real await point never yields the thread it's running on in the first place, no
    // wrapper on our end can fix that from outside. Any wait loop MUST use
    // "await Task.Delay(ms);" (or Task.Yield()), never a bare spin - documented in the Scripts
    // tab's own help text and the seeded example module for exactly this reason.
    public static async void StartRun(string code, Action<Result> onComplete)
    {
        var globals = new ScriptGlobals();
        Result result;
        try
        {
            ScriptState<object> state = await CSharpScript.RunAsync(code, Options(), globals, typeof(ScriptGlobals));
            string returnValue = state.ReturnValue?.ToString();
            if (returnValue != null)
                globals.Log($"=> {returnValue}");
            result = new Result(true, string.Empty, returnValue);
        }
        catch (CompilationErrorException ex)
        {
            result = Result.Fail(string.Join("\n", ex.Diagnostics));
        }
        catch (Exception ex)
        {
            // Any real exception thrown BY the script (NullReferenceException reaching into a
            // missing component, etc.) lands here instead of crashing the whole game - a broken
            // script should be a red message in the log, never a reason to Alt+F4.
            result = Result.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        onComplete(result);
    }
}
