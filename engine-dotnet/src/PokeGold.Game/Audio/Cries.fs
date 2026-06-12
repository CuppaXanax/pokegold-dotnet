namespace PokeGold.Game.Audio

open System
open System.Collections.Generic
open System.Globalization
open PokeGold.Game.Core

type CryMetadata =
    { Species: string
      CryConstant: string
      BaseLabel: string
      Pitch: int
      Length: int }

module Cries =
    [<Literal>]
    let private CryPrefix = "Cry:"

    [<Literal>]
    let private SlowCryPrefix = "CrySlow:"

    let sfxName (species: string) = CryPrefix + species
    let slowSfxName (species: string) = SlowCryPrefix + species

    let private parseInt (token: string) =
        let t = token.Trim()
        if t.StartsWith("$", StringComparison.Ordinal) then Convert.ToInt32(t.Substring(1), 16)
        elif t.StartsWith("%", StringComparison.Ordinal) then Convert.ToInt32(t.Substring(1), 2)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    let private splitComment (line: string) =
        let idx = line.IndexOf(';')
        if idx < 0 then line, ""
        else line.Substring(0, idx), line.Substring(idx + 1).Trim()

    let private splitArgs (args: string) =
        args.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun arg -> arg.Trim())
        |> Array.toList

    let private cryConstants =
        lazy
            Assets.readText "constants/cry_constants.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')
            |> Seq.choose (fun raw ->
                let body = fst (splitComment raw) |> fun s -> s.Trim()
                if body.StartsWith("const CRY_", StringComparison.Ordinal) then
                    body.Substring("const ".Length).Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.tryHead
                else
                    None)
            |> Seq.toList

    let private cryPointers =
        lazy
            Assets.readText "audio/cry_pointers.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')
            |> Seq.choose (fun raw ->
                let body = fst (splitComment raw) |> fun s -> s.Trim()
                if body.StartsWith("dba Cry_", StringComparison.Ordinal) then
                    Some(body.Substring("dba ".Length).Trim())
                else
                    None)
            |> Seq.toList

    let private cryLabelByConstant =
        lazy
            let constants = cryConstants.Value
            let pointers = cryPointers.Value
            if constants.Length <> pointers.Length then
                failwithf "Cry constant/pointer table mismatch: %d constants, %d pointers" constants.Length pointers.Length

            List.zip constants pointers |> Map.ofList

    let private baseSongs =
        lazy
            SongAsm.allSongs (Assets.readText "audio/cries.asm")
            |> Map.ofList

    let private metadataBySpecies =
        lazy
            let labelByConstant = cryLabelByConstant.Value
            let rows = ResizeArray<CryMetadata>()

            for raw in (Assets.readText "data/pokemon/cries.asm" |> fun text -> text.Replace("\r", "").Split('\n')) do
                let body, comment = splitComment raw
                let line = body.Trim()
                if line.StartsWith("mon_cry ", StringComparison.Ordinal) && comment <> "" then
                    match splitArgs (line.Substring("mon_cry ".Length)) with
                    | cryConstant :: pitch :: length :: _ ->
                        let label =
                            match Map.tryFind cryConstant labelByConstant with
                            | Some value -> value
                            | None -> failwithf "Cry constant %s is not listed in audio/cry_pointers.asm" cryConstant

                        rows.Add
                            { Species = comment.Trim().ToUpperInvariant()
                              CryConstant = cryConstant
                              BaseLabel = label
                              Pitch = parseInt pitch
                              Length = parseInt length }
                    | _ -> ()

            rows
            |> Seq.map (fun row -> row.Species, row)
            |> Map.ofSeq

    let tryMetadataForSpecies (species: string) =
        metadataBySpecies.Value |> Map.tryFind (species.Trim().ToUpperInvariant())

    let metadataForSpecies (species: string) =
        match tryMetadataForSpecies species with
        | Some metadata -> metadata
        | None -> failwithf "No Pokémon cry metadata found for species '%s'" species

    let private withCryParameters (pitch: int) (length: int) (song: Song) =
        let commands = List<SoundCommand>(song.Commands)

        let isNoiseChannel channelId =
            ((channelId - 1) % 4) = 3

        let channels =
            song.Channels
            |> Array.map (fun (channelId, entry) ->
                let prefix = commands.Count
                commands.Add(PitchOffset pitch)
                if not (isNoiseChannel channelId) then
                    commands.Add(Tempo length)
                commands.Add(SoundJump entry)
                channelId, prefix)

        { song with
            Channels = channels
            Commands = commands.ToArray() }

    let songForSpecies slow (species: string) =
        let metadata = metadataForSpecies species
        let pitch = metadata.Pitch + (if slow then -0x140 else 0)
        let length = metadata.Length + (if slow then 0x60 else 0)
        let baseSong =
            match Map.tryFind metadata.BaseLabel baseSongs.Value with
            | Some song -> song
            | None -> failwithf "Cry base label %s was not parsed from audio/cries.asm" metadata.BaseLabel

        withCryParameters pitch length baseSong

    let trySongForSfxName (name: string) =
        if name.StartsWith(SlowCryPrefix, StringComparison.OrdinalIgnoreCase) then
            Some(songForSpecies true (name.Substring(SlowCryPrefix.Length)))
        elif name.StartsWith(CryPrefix, StringComparison.OrdinalIgnoreCase) then
            Some(songForSpecies false (name.Substring(CryPrefix.Length)))
        else
            None
