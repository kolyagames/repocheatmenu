using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace RepoCheatMenu;

internal sealed class ScriptModule
{
    public string Name;
    public string Source;
}

// Add/remove persisted script modules - each is just a ".csx" text file under a "Scripts" folder
// next to this plugin's own DLL (BepInEx.Paths.PluginPath is the plugins root, not a per-plugin
// folder, so this makes its own subfolder rather than dropping files loose next to every other
// plugin's DLL). Deliberately no "always run every tick" mode for a saved module - Run() is a
// one-shot button press, same as any other action-button hack in this mod (ItemPurge, Nuke,
// ...); a script that wants repeated behavior can loop/Task.Delay inside itself since it's real
// C#, without this manager needing to babysit a per-frame schedule for arbitrary user code.
internal static class ScriptModuleManager
{
    private static readonly List<ScriptModule> _modules = new();
    public static IReadOnlyList<ScriptModule> Modules => _modules;

    private static string FolderPath => Path.Combine(Paths.PluginPath, "RepoCheatMenu_Scripts");

    // Ready-made example for the exact ask that motivated exposing Photon to scripts in the
    // first place ("create a lobby with any name from a click") - built from the real sequence
    // the game's own menu uses (NetworkConnect.cs/RunManagerPUN.HostSteamLobby in the decompiled
    // source: CreateRoom/JoinOrCreateRoom with the desired name, then mirror it into the Steam
    // lobby's own displayed data), not a guess. Only ever written to disk once - if the file
    // already exists (including an edited/renamed-away copy), LoadAll leaves it alone rather
    // than stomping changes on every game start.
    private const string ExampleModuleName = "СоздатьЛобби";
    private const string ExampleModuleSource =
        "// Создаёт (или подключает вас к уже существующей) комнату Photon с выбранным именем.\n" +
        "// Название комнаты видно другим через список комнат / приглашение; чтобы то же имя\n" +
        "// показывалось и в приглашениях Steam, раскомментируйте строку с Steam.SetLobbyData.\n" +
        "string roomName = \"Моя комната\"; // поменяйте на что хотите\n" +
        "\n" +
        "var options = new RoomOptions { MaxPlayers = 6, IsVisible = true, IsOpen = true };\n" +
        "PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);\n" +
        "\n" +
        "while (!PhotonNetwork.InRoom)\n" +
        "    await Task.Delay(100); // await, не пустой while - иначе игра подвиснет, см. справку выше\n" +
        "\n" +
        "Log($\"В комнате: {PhotonNetwork.CurrentRoom.Name}\");\n" +
        "// Steam.SetLobbyData(roomName);\n";

    // Several genuinely independent rooms (each with its own real, separate population, up to
    // maxPlayers each) instead of one giant room - a single Photon room's physics/enemy/level
    // sync was never built or tested anywhere near 100 concurrent players, so splitting across
    // real rooms is the actually-workable way to raise total concurrent capacity. All three
    // share one display name on purpose (that's just what shows in the Steam lobby browser - see
    // SteamManager.SetLobbyData, a free-form string, no uniqueness requirement) - this is NOT the
    // "many browser entries pointing at the same one room" trick that got turned down earlier in
    // this same conversation; each entry here is a real, separate, independently-joinable room.
    // The actual Photon room NAME is left null on purpose (Photon auto-generates a unique
    // technical id) specifically because CreateRoom requires unique names and giving all three
    // the display name literally would make the 2nd/3rd calls fail outright.
    private const string MultiRoomModuleName = "3Комнаты";
    private const string MultiRoomModuleSource =
        "// Создаёт несколько ОТДЕЛЬНЫХ комнат подряд - у каждой своя реальная аудитория до\n" +
        "// maxPlayers человек, но всем задано одно и то же отображаемое имя (это просто общее\n" +
        "// название группы комнат в списке серверов, а не подмена одной комнаты за несколько).\n" +
        "string displayName = \"Моя комната\"; // поменяйте на что хотите\n" +
        "const int roomCount = 3;\n" +
        "const int maxPlayers = 40; // 30-50 разумно, синхронизация выше не проверялась\n" +
        "\n" +
        "for (int i = 0; i < roomCount; i++)\n" +
        "{\n" +
        "    var options = new RoomOptions\n" +
        "    {\n" +
        "        MaxPlayers = maxPlayers,\n" +
        "        IsVisible = true,\n" +
        "        IsOpen = true,\n" +
        "        EmptyRoomTtl = 5 * 60 * 1000 // комната живёт 5 минут даже без игроков, чтобы её успели найти\n" +
        "    };\n" +
        "\n" +
        "    // null - пусть Photon сам придумает уникальное техническое имя комнаты (CreateRoom\n" +
        "    // требует уникальное имя, все три с одним и тем же именем не создались бы); то, что\n" +
        "    // реально ВИДЯТ игроки - отдельное Steam-имя лобби чуть ниже, оно не обязано быть уникальным.\n" +
        "    PhotonNetwork.CreateRoom(null, options, TypedLobby.Default);\n" +
        "    while (!PhotonNetwork.InRoom)\n" +
        "        await Task.Delay(100);\n" +
        "\n" +
        "    Steam.HostLobby(true);\n" +
        "    await Task.Delay(2000); // с запасом, не подтверждённый poll готовности Steam-лобби\n" +
        "\n" +
        "    Steam.SetLobbyData(displayName);\n" +
        "    Log($\"Комната {i + 1}/{roomCount} готова: {PhotonNetwork.CurrentRoom.Name}\");\n" +
        "\n" +
        "    PhotonNetwork.LeaveRoom();\n" +
        "    while (PhotonNetwork.InRoom)\n" +
        "        await Task.Delay(100);\n" +
        "\n" +
        "    Steam.LeaveLobby();\n" +
        "    await Task.Delay(500);\n" +
        "}\n" +
        "\n" +
        "Log(\"Готово: все комнаты созданы.\");\n";

    public static void LoadAll()
    {
        _modules.Clear();
        try
        {
            Directory.CreateDirectory(FolderPath);

            string examplePath = Path.Combine(FolderPath, ExampleModuleName + ".csx");
            if (!File.Exists(examplePath))
                File.WriteAllText(examplePath, ExampleModuleSource);

            string multiRoomPath = Path.Combine(FolderPath, MultiRoomModuleName + ".csx");
            if (!File.Exists(multiRoomPath))
                File.WriteAllText(multiRoomPath, MultiRoomModuleSource);

            foreach (string path in Directory.GetFiles(FolderPath, "*.csx").OrderBy(p => p))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string source = File.ReadAllText(path);
                _modules.Add(new ScriptModule { Name = name, Source = source });
            }
            Plugin.Log.LogInfo($"Script modules: loaded {_modules.Count} from {FolderPath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Script modules: failed to load saved scripts - {ex.Message}");
        }
    }

    // Windows forbids \/:*?"<>| in filenames - a script named after any of those would otherwise
    // fail to save with an unhelpful IOException pointing nowhere near the actual cause.
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(InvalidChars) < 0;

    // Validates syntax before ever touching disk or the in-memory list - a module that fails to
    // compile is reported right away instead of getting saved silently broken and only failing
    // later when someone presses its Run button.
    public static ScriptEngine.Result Add(string name, string source)
    {
        if (!IsValidName(name))
            return ScriptEngine.Result.Fail("Имя модуля не может быть пустым или содержать \\ / : * ? \" < > |");

        ScriptEngine.Result check = ScriptEngine.CheckSyntax(source);
        if (!check.Success) return check;

        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(Path.Combine(FolderPath, name + ".csx"), source);
        }
        catch (Exception ex)
        {
            return ScriptEngine.Result.Fail($"Не удалось сохранить файл: {ex.Message}");
        }

        ScriptModule existing = _modules.FirstOrDefault(m => m.Name == name);
        if (existing != null) existing.Source = source;
        else _modules.Add(new ScriptModule { Name = name, Source = source });

        return ScriptEngine.Result.Ok;
    }

    public static void Remove(string name)
    {
        try
        {
            string path = Path.Combine(FolderPath, name + ".csx");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Script modules: failed to delete '{name}' - {ex.Message}");
        }
        _modules.RemoveAll(m => m.Name == name);
    }

    // Async now (see ScriptEngine.StartRun's comment for why a synchronous Run would deadlock
    // the moment a script waits on a Photon/Steam operation) - onComplete fires later, once the
    // script actually finishes, not before this call returns.
    public static void Run(string name, Action<ScriptEngine.Result> onComplete)
    {
        ScriptModule module = _modules.FirstOrDefault(m => m.Name == name);
        if (module == null) onComplete(ScriptEngine.Result.Fail("Модуль не найден."));
        else ScriptEngine.StartRun(module.Source, onComplete);
    }
}
