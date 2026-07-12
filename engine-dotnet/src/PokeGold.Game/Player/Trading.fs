namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Offline trading: swap a PartyMon between two party lists.
/// The D7 design doc specifies a same-device trade terminal.
/// Simplified: trade between player's own party slots (self-trade for evolutions).
module Trading =

    /// Local D7 terminal imports for species families unavailable from the Gold
    /// world data itself (version exclusives, legacy starters/fossils, Gen-1
    /// legendaries). Networking stays out of scope.
    let offlineImportCatalog : Set<string> =
        Set.ofList
            [ "AERODACTYL"; "ARTICUNO"; "BULBASAUR"; "CHARMANDER"; "DELIBIRD"
              "KABUTO"; "LEDYBA"; "MEOWTH"; "MEWTWO"; "MOLTRES"; "OMANYTE"
              "PHANPY"; "SKARMORY"; "SQUIRTLE"; "VULPIX"; "ZAPDOS" ]

    let canOfflineImport species =
        Set.contains species offlineImportCatalog

    let offlineTerminalImport species level =
        if canOfflineImport species then
            Species.all
            |> Map.tryFind species
            |> Option.map (fun stats -> PartyMon.create stats.Dex level)
        else
            None

    /// Execute a trade: swap party[indexA] with partner[indexB].
    /// Returns updated (party, partner) or None if indices invalid.
    let executeTrade (party: Party) (indexA: int) (partner: Party) (indexB: int) : (Party * Party) option =
        if indexA < 0 || indexA >= party.Length || indexB < 0 || indexB >= partner.Length then
            None
        else
            let monA = party.[indexA]
            let monB = partner.[indexB]
            let party' = party |> List.mapi (fun i m -> if i = indexA then monB else m)
            let partner' = partner |> List.mapi (fun i m -> if i = indexB then monA else m)
            Some(party', partner')

    /// Check if a traded mon should evolve (trade evolution).
    /// Returns Some(targetSpecies) if the mon evolves on trade.
    let checkTradeEvolution (mon: PartyMon) : string option =
        Evolution.tryFind (Trade false) mon |> Option.map _.Target

    let private evolveReceived isTimeCapsule accept (mon: PartyMon) =
        match Evolution.tryFind (Trade isTimeCapsule) mon with
        | Some candidate ->
            let attempted = Evolution.prepareAttempt candidate mon
            if accept then Evolution.applyCandidate candidate attempted else attempted
        | None -> mon

    /// Trade with explicit acceptance/cancellation and Time Capsule context.
    let tradeWithEvolutionDecision isTimeCapsule accept (party: Party) indexA (partner: Party) indexB =
        match executeTrade party indexA partner indexB with
        | None -> None
        | Some(party', partner') ->
            let receivedByPlayer = evolveReceived isTimeCapsule accept party'.[indexA]
            let receivedByPartner = evolveReceived isTimeCapsule accept partner'.[indexB]
            Some(
                party' |> List.mapi (fun i mon -> if i = indexA then receivedByPlayer else mon),
                partner' |> List.mapi (fun i mon -> if i = indexB then receivedByPartner else mon))

    /// Apply trade: swap mons, then check for trade evolution on the received mon.
    let tradeWithEvolution (party: Party) (indexA: int) (partner: Party) (indexB: int) : (Party * Party) option =
        tradeWithEvolutionDecision false true party indexA partner indexB
