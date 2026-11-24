$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$panel = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/VillageTeleportPanel.cs') -Raw
$shell = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$binding = [regex]::Match($shell, '(?ms)^--- !u!114 &999270565\r?\n.*?(?=^--- !u!)').Value
$fields = [regex]::Match($binding, '(?ms)^  destinations:\r?\n(.*?)^  departures:\r?\n(.*)\z').Groups
$destinations = @(
    foreach ($entry in [regex]::Matches($fields[1].Value, '(?m)^  - name: "([^"]+)"\r?\n    sceneId: (\w+)\r?\n    mapId: (\w+)\r?\n    button: \{fileID: (\d+)\}')) {
        $button = $entry.Groups[4].Value
        if ($shell -notmatch "(?ms)^--- !u!114 &$button\r?\n(?:(?!^--- !u!).)*?guid: 4e29b1a8efbd4b44bb3f3716e73f07ff") {
            throw "The destination does not reference a Unity Button: $button"
        }
        @{ name = [regex]::Unescape($entry.Groups[1].Value); sceneId = $entry.Groups[2].Value; mapId = $entry.Groups[3].Value; buttonId = $button }
    }
)
$departures = @(
    foreach ($entry in [regex]::Matches($fields[2].Value, '(?m)^  - name: "([^"]+)"\r?\n    sceneId: (\w+)\r?\n    mapId: (\w+)\r?\n    allowedRoutes:\r?\n((?:    - sceneId: \w+\r?\n(?:      routeId: *\w*\r?\n)?)+)')) {
        $allowed = @(
            foreach ($route in [regex]::Matches($entry.Groups[4].Value, '(?m)^    - sceneId: (\w+)\r?\n(?:      routeId: *(\w*)\r?\n)?')) {
                @{ sceneId = $route.Groups[1].Value; routeId = $route.Groups[2].Value }
            }
        )
        @{ sceneId = $entry.Groups[2].Value; mapId = $entry.Groups[3].Value; allowedRoutes = $allowed }
    }
)
if ($destinations.Count -ne 8 -or @($destinations.buttonId | Sort-Object -Unique).Count -ne 8 -or $departures.Count -ne 8) {
    throw 'WorldShell must link eight distinct destination buttons and eight departure rows.'
}

$methods = foreach ($name in 'HandleOptionsResult', 'FindAllowedRoute', 'SelectDestination', 'FindRoute', 'RefreshDestinationButtons') {
    $method = [regex]::Match($panel, "(?ms)^    private [^\r\n]* $name\([^)]*\).*?^    \}\r?$").Value
    if (!$method) { throw "Production method missing: $name" }
    $method
}

$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed class MapTransferRoute { public string routeId, sceneId, mapId; }
public sealed class MapTransferOptionsResult { public string sceneSessionId; public MapTransferRoute[] routes; }
public static class JsonUtility
{
    public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, new JsonSerializerOptions { IncludeFields = true });
}
public static class GameNoticeService
{
    public static string Last;
    public static void Show(string notice) => Last = notice;
}
public struct ColorBlock { public int normalColor, highlightedColor, selectedColor, pressedColor, disabledColor; }
public sealed class Button { public ColorBlock colors; public bool interactable; }

public sealed class TeleportPanelRouteHarness
{
    public sealed class Destination
    {
        public string name, sceneId, mapId;
        public Button button;
        public ColorBlock normalColors;
        public MapTransferRoute route;
        public bool ambiguous;
    }
    public sealed class DestinationRoute { public string sceneId, routeId; }
    public sealed class Departure { public string sceneId, mapId; public DestinationRoute[] allowedRoutes; }

    private Destination[] destinations;
    private Departure currentDeparture;
    private Action<MapTransferRoute> transferHandler;
    private string querySessionId = "scene-session";
    private bool queryPending;
    private bool IsOpen => true;
    private bool IsQueryCurrent() => true;

__PANEL_METHODS__

    public static void Verify(string bindings, string rules)
    {
        var destinations = JsonUtility.FromJson<Destination[]>(bindings);
        var departures = JsonUtility.FromJson<Departure[]>(rules);
        var expected = new Dictionary<string, string[]>
        {
            ["world_001"] = new[] { "world_002" },
            ["world_002"] = new[] { "world005", "world007", "world008", "world009" },
            ["world005"] = new[] { "world_002" },
            ["world006"] = new[] { "world007" },
            ["world007"] = new[] { "world_002", "world006" },
            ["world008"] = new[] { "world_002" },
            ["world009"] = new[] { "world_002", "world010" },
            ["world010"] = new[] { "world009" }
        };
        var used = new List<MapTransferRoute>();
        var menu = new TeleportPanelRouteHarness { destinations = destinations, transferHandler = used.Add };
        var routes = destinations.Select(d => new MapTransferRoute
            { routeId = "confirmed-" + d.sceneId, sceneId = d.sceneId, mapId = d.mapId }).ToArray();
        var officialGrave = new MapTransferRoute { routeId = "luxian005", sceneId = "world007", mapId = "map_007" };
        var duplicateGrave = new MapTransferRoute { routeId = "luxian010", sceneId = "world007", mapId = "map_007" };
        foreach (var d in destinations)
        {
            d.button = new Button();
            d.normalColors = new ColorBlock { normalColor = 1, highlightedColor = 2, pressedColor = 3, selectedColor = 4 };
        }
        foreach (var row in departures)
        {
            Check(destinations.Single(d => d.sceneId == row.sceneId).mapId == row.mapId, "Source map ID does not match.");
            menu.BeginQuery(row);
            menu.Reply(menu.querySessionId, row.sceneId == "world_002"
                ? routes.Where(route => route.sceneId != "world007").Concat(new[] { duplicateGrave, officialGrave }).ToArray()
                : routes);
            foreach (var dest in destinations)
            {
                bool allowed = expected[row.sceneId].Contains(dest.sceneId);
                var colors = dest.button.colors;
                Check(dest.button.interactable && colors.normalColor == (allowed ? 1 : colors.disabledColor), "Wrong map button style.");
                if (!allowed)
                    Check(colors.highlightedColor == colors.disabledColor && colors.pressedColor == colors.disabledColor
                          && colors.selectedColor == colors.disabledColor, "Blocked button changed color on hover or click.");
                int previous = used.Count;
                GameNoticeService.Last = null;
                menu.SelectDestination(dest);
                if (allowed)
                {
                    string expectedId = row.sceneId == "world_002" && dest.sceneId == "world007"
                        ? "luxian005" : "confirmed-" + dest.sceneId;
                    Check(used.Count == previous + 1 && used.Last().routeId == expectedId, "Wrong server route selected.");
                }
                else
                    Check(used.Count == previous && GameNoticeService.Last == "目前所在位置无法前往该地区", "Blocked click must only show the hint.");
            }
        }
        var village = departures.Single(row => row.sceneId == "world_002");
        var grave = destinations.Single(d => d.sceneId == "world007");
        var valley = destinations.Single(d => d.sceneId == "world005");
        int before = used.Count;
        menu.BeginQuery(village);
        menu.Reply("previous-session", routes);
        menu.SelectDestination(grave);
        Check(used.Count == before && GameNoticeService.Last == "正在查询传送路线，请稍候", "Old session reply accepted.");
        menu.Reply(menu.querySessionId, new[] { duplicateGrave });
        menu.SelectDestination(grave);
        Check(used.Count == before && GameNoticeService.Last.Contains("没有前往剑冢荒原的可用路线"),
            "Another route must not replace the configured server route.");
        menu.BeginQuery(village);
        var duplicate = new MapTransferRoute { sceneId = valley.sceneId, mapId = valley.mapId, routeId = "duplicate" };
        menu.Reply(menu.querySessionId, routes.Concat(new[] { duplicate }).ToArray());
        menu.SelectDestination(valley);
        Check(used.Count == before && GameNoticeService.Last.Contains("路线配置不明确"), "Ambiguous server routes accepted.");

        var returnToVillage = routes.Single(route => route.sceneId == "world_002");
        foreach (string source in new[] { "world006", "world010" })
        {
            menu.BeginQuery(departures.Single(row => row.sceneId == source));
            menu.Reply(menu.querySessionId, new[] { returnToVillage });
            menu.SelectDestination(destinations.Single(dest => dest.sceneId == "world_002"));
            Check(used.Count == before && GameNoticeService.Last == "目前所在位置无法前往该地区",
                "A server route outside the user's direction must not be submitted.");
        }

        menu.BeginQuery(departures.Single(row => row.sceneId == "world007"));
        menu.Reply(menu.querySessionId, new[] { routes.Single(route => route.sceneId == "world006") });
        menu.SelectDestination(destinations.Single(dest => dest.sceneId == "world_002"));
        Check(used.Count == before && GameNoticeService.Last.Contains("没有前往长风村的可用路线"),
            "The client must wait for an actual return route from Gate.");
    }

    private void BeginQuery(Departure row)
    {
        currentDeparture = row;
        queryPending = true;
        foreach (var d in destinations)
        {
            d.route = null;
            d.ambiguous = false;
        }
        RefreshDestinationButtons();
    }

    private void Reply(string sessionId, MapTransferRoute[] routes)
    {
        var message = new MapTransferOptionsResult { sceneSessionId = sessionId, routes = routes };
        HandleOptionsResult(JsonSerializer.Serialize(message, new JsonSerializerOptions { IncludeFields = true }));
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
'@
$source = $stubs.Replace('__PANEL_METHODS__', ($methods -join [char]10))
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null
[TeleportPanelRouteHarness]::Verify(
    (ConvertTo-Json -InputObject $destinations -Depth 3 -Compress),
    (ConvertTo-Json -InputObject $departures -Depth 3 -Compress)
)
Write-Output 'PASS: WorldShell route table, grey-button hints, legacy server route selection and stale reply protection.'
