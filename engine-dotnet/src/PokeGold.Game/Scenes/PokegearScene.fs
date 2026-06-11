namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Render

/// PokeGear shell with player-facing Map / Phone / Radio tabs.
type PokegearScene(font: Font, player: PlayerState, ?initialTab: PokegearTab, ?mapId: string, ?radioChannel: int) =
    let indexOf =
        function
        | MapTab -> 0
        | PhoneTab -> 1
        | RadioTab -> 2

    let mutable cursor = defaultArg initialTab PhoneTab |> indexOf
    let input = PokeGold.Game.Ui.EdgeDetector()
    let tabs = [| "MAP"; "PHONE"; "RADIO" |]
    let palette = TextRenderer.palette
    let mapName = defaultArg mapId "UNKNOWN"

    member _.Cursor = cursor
    member _.CurrentTab =
        match cursor with
        | 0 -> MapTab
        | 1 -> PhoneTab
        | _ -> RadioTab

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            if edges.B then
                Pop
            elif edges.Down then
                cursor <- min (tabs.Length - 1) (cursor + 1)
                Stay
            elif edges.Up then
                cursor <- max 0 (cursor - 1)
                Stay
            elif edges.A then
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
                match radioChannel with
                | Some channel -> WindowRenderer.drawString fb font palette 1 9 (sprintf "CHANNEL %d" channel)
                | None -> WindowRenderer.drawString fb font palette 1 9 "SELECT A STATION"
