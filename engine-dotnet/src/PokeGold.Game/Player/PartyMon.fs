namespace PokeGold.Game.Player

open System
open PokeGold.Game.Data
open PokeGold.Game.Battle

/// Source mail metadata attached to one party Pokémon alongside its mail held item.
type PartyMail = { Item: string; Message: string; SenderName: string; SenderId: int; Species: int }

/// A Pokémon in the persistent party (on the player's team).
/// All fields preserve the GSC save-file struct plus native identity and mail metadata.
type PartyMon = { Id: Guid; SpeciesId: int; Nickname: string; Level: int; Exp: int; Hp: int; MaxHp: int; Status: string; Moves: (int * int) list; Dvs: int; StatExp: StatExperience; Pokerus: int; HeldItem: string option; Mail: PartyMail option; OtName: string; OtId: int; Friendship: int; HatchSteps: int option }

/// A player's party — up to 6 PartyMon in order.
type Party = PartyMon list

/// Source-compatible `checkpokemail` result values and party mutation.
module PartyMailOps =
    [<Literal>]
    let WrongMail = 0

    [<Literal>]
    let Correct = 1

    [<Literal>]
    let Refused = 2

    [<Literal>]
    let NoMail = 3

    [<Literal>]
    let LastMon = 4

    let private mailItems =
        Set.ofList
            [ "FLOWER_MAIL"; "SURF_MAIL"; "LITEBLUEMAIL"; "PORTRAITMAIL"; "LOVELY_MAIL"
              "EON_MAIL"; "MORPH_MAIL"; "BLUESKY_MAIL"; "MUSIC_MAIL"; "MIRAGE_MAIL" ]

    let check expectedMessage selectedIndex (party: Party) : int * Party =
        if selectedIndex < 0 || selectedIndex >= party.Length then
            Refused, party
        else
            let selected = party.[selectedIndex]
            let mailItem = selected.HeldItem |> Option.filter mailItems.Contains

            match mailItem, selected.Mail with
            | None, _ -> NoMail, party
            | Some item, Some mail when item = mail.Item && mail.Message.StartsWith(expectedMessage, StringComparison.Ordinal) ->
                let hasOtherConsciousMon =
                    party
                    |> List.mapi (fun partyIndex mon -> partyIndex, mon)
                    |> List.exists (fun (partyIndex, mon) -> partyIndex <> selectedIndex && mon.Hp > 0)

                if not hasOtherConsciousMon then
                    LastMon, party
                else
                    let remaining =
                        party
                        |> List.mapi (fun partyIndex mon -> partyIndex, mon)
                        |> List.choose (fun (partyIndex, mon) -> if partyIndex = selectedIndex then None else Some mon)
                    Correct, remaining
            | _ -> WrongMail, party

module PartyMon =

    let private speciesOf (speciesId: int) : BaseStats option =
        Species.all |> Map.tryPick (fun _ s -> if s.Dex = speciesId then Some s else None)

    /// Derive MaxHp from species base stats and level (Gen-2 formula, DV=0, SE=0).
    let deriveMaxHp (speciesId: int) (level: int) : int =
        match speciesOf speciesId with
        | Some s -> BattleMon.calcHp s.Hp level
        | None -> 1

    let deriveMaxHpWith (speciesId: int) (level: int) (dvs: int) (statExp: StatExperience) : int =
        match speciesOf speciesId with
        | Some s -> (BattleMon.calculateStats s level dvs statExp).MaxHp
        | None -> 1

    /// Build a fresh PartyMon at full HP for the given species and level.
    let createWithDvs (speciesId: int) (level: int) (dvs: int) : PartyMon =
        let maxHp = deriveMaxHpWith speciesId level dvs StatExperience.zero
        let name =
            Species.all
            |> Map.tryPick (fun k s -> if s.Dex = speciesId then Some k else None)
            |> Option.defaultValue (string speciesId)
        { Id = Guid.NewGuid()
          SpeciesId = speciesId
          Nickname = name
          Level = level
          Exp = 0
          Hp = maxHp
          MaxHp = maxHp
          Status = ""
          Moves = []
          Dvs = dvs
          StatExp = StatExperience.zero
          Pokerus = 0
          HeldItem = None
          Mail = None
          OtName = "PLAYER"
          OtId = 0
          Friendship = 70
          HatchSteps = None }

    let create (speciesId: int) (level: int) : PartyMon =
        createWithDvs speciesId level 0

    /// Recalculate level-dependent HP while preserving the current damage deficit.
    let withLevel (level: int) (mon: PartyMon) : PartyMon =
        let newMaxHp = deriveMaxHpWith mon.SpeciesId level mon.Dvs mon.StatExp
        let hpGain = newMaxHp - mon.MaxHp
        { mon with Level = level; MaxHp = newMaxHp; Hp = max 0 (mon.Hp + hpGain) }

    /// Convert a PartyMon to a BattleMon for use in battle (seam for M13/M14).
    /// Move lookup is approximate until M13 wires the full move set.
    let toBattleMon (mon: PartyMon) : BattleMon =
        let species =
            speciesOf mon.SpeciesId
            |> Option.defaultWith (fun () ->
                { Dex = mon.SpeciesId; Name = string mon.SpeciesId
                  Hp = 45; Attack = 45; Defense = 45; Speed = 45; SpAttack = 45; SpDefense = 45
                  Type1 = 0; Type2 = 0
                  CatchRate = 45; BaseExp = 64
                  Item1 = None; Item2 = None; GenderRatio = 255; GrowthRate = 0
                  EggGroup1 = 15; EggGroup2 = 15; HatchCycles = 0 })
        let moveSlots =
            mon.Moves
            |> List.choose (fun (moveId, pp) ->
                Moves.tryByIndex moveId |> Option.map (fun move -> move, pp))
            |> List.truncate 4
        let status =
            match mon.Status with
            | status when status.StartsWith("SLP:") ->
                match Int32.TryParse(status.Substring(4)) with
                | true, turns -> Sleep(max 1 (min 7 turns))
                | _ -> Sleep 1
            | "SLP" -> Sleep 1
            | "PSN" -> Poison
            | "BRN" -> Burn
            | "FRZ" -> Freeze
            | "PAR" -> Paralysis
            | _ -> Healthy
        let bm = BattleMon.ofSpeciesWithStats species mon.Level (moveSlots |> List.map fst) mon.Dvs mon.StatExp
        { bm with
            PersistentId = Some mon.Id
            Persistent =
                Some
                    { Species = species
                      Stats = BattleMon.calculateStats species mon.Level mon.Dvs mon.StatExp
                      Moves = moveSlots
                      Exp = mon.Exp
                      StatExp = mon.StatExp
                      Friendship = mon.Friendship }
            Hp = min mon.Hp bm.MaxHp
            Pp = moveSlots |> List.map snd
            HeldItem = mon.HeldItem
            Status = status
            Dvs = mon.Dvs
            Gender = BattleMon.genderFromDvs species mon.Dvs }

    /// Extract individual DVs from the packed int.
    /// Format: bits 15-12 = Atk, 11-8 = Def, 7-4 = Spd, 3-0 = Spc
    let atkDv (dvs: int) = (dvs >>> 12) &&& 0xF
    let defDv (dvs: int) = (dvs >>> 8) &&& 0xF
    let spdDv (dvs: int) = (dvs >>> 4) &&& 0xF
    let spcDv (dvs: int) = dvs &&& 0xF

    /// Derive the Unown letter form (0=A .. 25=Z) from packed DVs.
    /// Source: engine/pokemon/unown_form.asm::GetUnownLetter
    let unownLetter (dvs: int) : int =
        let atk = atkDv dvs
        let def' = defDv dvs
        let spd = spdDv dvs
        let spc = spcDv dvs
        let packed =
            (((atk >>> 1) &&& 3) <<< 6)
            ||| (((def' >>> 1) &&& 3) <<< 4)
            ||| (((spd >>> 1) &&& 3) <<< 2)
            ||| ((spc >>> 1) &&& 3)
        packed / 10

    /// Convert an Unown letter index (0-25) to its ASCII character.
    let unownChar (letterIndex: int) : char =
        char (int 'A' + letterIndex)

    /// Gen-2 shininess check: Atk DV has bit 1 set (2,3,6,7,10,11,14,15)
    /// AND Def, Spd, Spc DVs are all >= 10.
    /// Source: engine/pokemon/search.asm::CheckShininess
    let isShiny (dvs: int) : bool =
        let atk = atkDv dvs
        let def' = defDv dvs
        let spd = spdDv dvs
        let spc = spcDv dvs
        (atk &&& 2 <> 0) && def' >= 10 && spd >= 10 && spc >= 10
