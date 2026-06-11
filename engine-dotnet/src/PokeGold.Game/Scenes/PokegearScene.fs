namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Render

/// PokeGear shell with player-facing Map / Phone / Radio tabs.
///
/// `stations` is the tunable radio dial supplied by the caller (id, display
/// name); availability rules (EXPN card, region) live with the caller, which
/// has the world state. Tuning a station invokes `onTune` with its id so the
/// overworld can persist it (`__radio_station`) — that buffer is what
/// `special SnorlaxAwake` reads, mirroring the wMapMusic check in
/// engine/events/specials.asm.
type PokegearScene(font: Font, player: PlayerState, ?initialTab: PokegearTab, ?mapId: string, ?radioChannel: int, ?stations: (string * string) list, ?onTune: string -> unit) =
    let indexOf =
        function
        | MapTab -> 0
        | PhoneTab -> 1
        | RadioTab -> 2

    let mutable cursor = defaultArg initialTab PhoneTab |> indexOf
    let mutable stationCursor = 0
    let mutable tunedStation: string option = None
    let stations = defaultArg stations []
    let input = PokeGold.Game.Ui.EdgeDetector()
    let tabs = [| "MAP"; "PHONE"; "RADIO" |]
    let palette = TextRenderer.palette
    let mapName = defaultArg mapId "UNKNOWN"

    member _.Cursor = cursor
    member _.TunedStation = tunedStation
    member _.CurrentTab =
        match cursor with
        | 0 -> MapTab
        | 1 -> PhoneTab
        | _ -> RadioTab

    interface Scene with
        member this.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            if edges.B then
                Pop
            elif this.CurrentTab = RadioTab && not stations.IsEmpty then
                if edges.Down then
                    stationCursor <- min (stations.Length - 1) (stationCursor + 1)
                    Stay
                elif edges.Up then
                    stationCursor <- max 0 (stationCursor - 1)
                    Stay
                elif edges.A then
                    let id, _ = stations.[stationCursor]
                    tunedStation <- Some id
                    onTune |> Option.iter (fun tune -> tune id)
                    Stay
                elif edges.Left || edges.Right then
                    cursor <- (cursor + (if edges.Right then 1 else tabs.Length - 1)) % tabs.Length
                    Stay
                else
                    Stay
            elif edges.Down then
                cursor <- min (tabs.Length - 1) (cursor + 1)
                Stay
            elif edges.Up then
                cursor <- max 0 (cursor - 1)
                Stay
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawString fb font palette 1 1 "POKEGEAR"

            for i in 0 .. tabs.Length - 1 do
                let prefix = if i = cursor then ">" else " "
                WindowRenderer.drawString fb font palette (1 + i * 6) 3 (prefix + tabs.[i])

            match cursor with
            | 0 ->
                WindowRenderer.drawString fb font palette 1 7 "TOWN MAP"
                WindowRenderer.drawString fb font palette 1 9 ("AREA: " + mapName.Replace("_", " "))
            | 1 ->
                WindowRenderer.drawString fb font palette 1 7 "PHONE"
                let contacts = player.PhoneContacts |> Set.toList
                if contacts.IsEmpty then
                    WindowRenderer.drawString fb font palette 1 9 "NO CONTACTS"
                else
                    contacts
                    |> List.truncate 5
                    |> List.iteri (fun i contact ->
                        WindowRenderer.drawString fb font palette 1 (9 + i) (contact.Replace("PHONE_", "").Replace("_", " ")))
            | _ ->
                WindowRenderer.drawString fb font palette 1 7 "RADIO"

                if not stations.IsEmpty then
                    stations
                    |> List.truncate 4
                    |> List.iteri (fun i (id, name) ->
                        let marker =
                            if tunedStation = Some id then "*"
                            elif i = stationCursor then ">"
                            else " "
                        WindowRenderer.drawString fb font palette 1 (9 + i) (marker + name))
                else
                    match radioChannel with
                    | Some channel -> WindowRenderer.drawString fb font palette 1 9 (sprintf "CHANNEL %d" channel)
                    | None -> WindowRenderer.drawString fb font palette 1 9 "SELECT A STATION"
