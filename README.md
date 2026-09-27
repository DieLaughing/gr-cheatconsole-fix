# GR Cheat Console Fix

A BepInEx plugin that gets **Global Rescue Cheat Console v0.4.7** working again on current
Global Rescue builds (tested on game v0.5.13).

- **Download:** [latest release](../../releases/latest). Extract the zip into your Global Rescue
  folder (the one with `GR.exe`), which gives you `BepInEx/plugins/GRCheatConsoleFix/GRCheatConsoleFix.dll`.
- **Linux / Steam Deck (Proton):** add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the game's
  Steam launch options so BepInEx loads.
- **Requires:** BepInEx 5.4.x (win x64) and both Cheat Console plugin DLLs
  (`GlobalRescueCheatConsole_Rebuild.dll`, `GlobalRescueEmployeeOnlyPatch.dll`).
  Do **not** install the `ProjectAssembly.dll` that ships with the console; it's from an older
  game version.

## Why the console broke

Global Rescue's code is obfuscated, and every game update scrambles its member names again
(`FinanceManager.PGPEPOJPPNB` becomes `FinanceManager.LHGLAMFKCIN`, and so on). The console
and its Employee Bridge hardcode about 130 of these names, so after an update most buttons
silently stop doing anything.

## How the fix works

Instead of names, the fix finds game data by **structure**: shapes that stay the same when names
are scrambled. It uses [Harmony](https://github.com/pardeike/Harmony) to replace the broken
console methods with versions that locate members this way:

| Feature | How the member is found |
|---|---|
| Money / Reputation / Research | Each manager stores its value in its only private field of that type, multiplied by its only constant of that type (money `double × 13.5`, trophies `int × 123`, research `int × 417`). The fix writes the field, then raises the game's un-obfuscated `onMoneyChanged` / `onTrophiesChanged` / `onResearchPointsChanged` events so the UI updates. |
| Employees | [`SaveMap.cs`](SaveMap.cs) reads the IL of the game's own employee **save** method, which copies each member into `EmployeeSaveData`. That class's field names (`salary`, `hunger`, `tiredness`, ...) are not obfuscated, so this gives a name-to-member map that survives updates. |
| Employee lists / bases | Every `List<T>` / `HashSet<T>` of `Employee` / `Base` on the relevant manager. |
| No Call Cooldowns | `MissionManager`'s only `Dictionary<string, DateTime>`, which holds recent `mission-location` spawns that are excluded from new calls. |
| Time | The public `TimeManager.SetTime(DateTime)`. |
| Free wages stay $0 | The employee panel's wage slider is two-way bound with a minimum of `MinSalary`, which would clamp $0 back up. A postfix returns 0 for employees already at $0, and another keeps saved $0 wages through the game's load-time clamp. |

If a future update changes a shape, the fix logs a warning to `BepInEx/LogOutput.log` and leaves
the value alone. It never guesses.

### What it patches (all at runtime, nothing on disk)

- Cheat Console: `SetMoney`, `SetResearch`, `SetReputation`, `MakeEmployeesFreeFromManager`,
  `ZeroEmployeeFatigueFromManager`, `GetEmployeeList`, `EnableAllBaseDepartments`,
  `ClearDictionary` (only its `CallManager` use)
- Employee Bridge: `ApplyEmployees`, `ApplyEmployeeSkillsOnly`, `GatherEmployees`,
  `ForceCurrentDateTime`, `LogFirstEmployeeDiagnostic` (disabled; it only logged broken lookups)
- Game: `EmployeeUIData.MinSalary` getter and the `Employee` save loader (postfixes, only for $0 wages)

### What it does not do

- No network access, and no file reads or writes. It only logs through BepInEx.
- Doesn't include or modify any game files or the original mod's files.
- Doesn't unlock paid DLC. The console's "Unlock DLC Dictionary Flags" button is deliberately
  left unfixed.

## Building and verifying

Requires the .NET SDK (releases are built with **8.0.425**) and a Global Rescue install with
BepInEx, which supplies the reference DLLs. No game code is included in this repo.

```sh
dotnet build -c Release -p:GameDir="/path/to/steamapps/common/GR"
sha256sum bin/Release/netstandard2.1/GRCheatConsoleFix.dll
```

Builds are deterministic: with the same SDK version and the same game/BepInEx reference DLLs,
you should get the same SHA-256 as the DLL in the release zip (listed in each release's notes).

## Credits

The Cheat Console and Employee Bridge belong to their original authors. This project only keeps
them running on newer game versions.

## License

[MIT](LICENSE)
