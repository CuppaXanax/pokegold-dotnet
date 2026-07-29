namespace PokeGold.Game.Player

/// Source-backed helpers for the small party systems invoked by map `special`s.
module LuckyNumber =

    /// Return the source prize tier (0 none, 1/2/3 prize) and the matching mon.
    /// The ROM compares the trailing five decimal OT-ID digits and keeps only the
    /// best 5-, 3/4-, or 2-digit match.
    let bestMatch (luckyNumber: int) (mons: PartyMon list) : int * PartyMon option =
        let matchingDigits id =
            let luckyDigits = sprintf "%05d" luckyNumber
            let idDigits = sprintf "%05d" (id &&& 0xffff)
            Seq.zip (Seq.rev luckyDigits) (Seq.rev idDigits)
            |> Seq.takeWhile (fun (a, b) -> a = b)
            |> Seq.length

        mons
        |> List.filter (Breeding.isEgg >> not)
        |> List.map (fun mon -> matchingDigits mon.OtId, mon)
        |> List.sortByDescending fst
        |> List.tryHead
        |> function
            | Some(5, mon) -> 1, Some mon
            | Some(digits, mon) when digits >= 3 -> 2, Some mon
            | Some(2, mon) -> 3, Some mon
            | _ -> 0, None

module Shuckie =

    [<Literal>]
    let ManiaOtId = 0x0518

    let give (party: Party) : Party option =
        if party.Length >= 6 then None
        else
            let shuckie =
                { PartyMon.create 213 15 with
                    HeldItem = Some "BERRY"
                    Nickname = "SHUCKIE"
                    OtName = "MANIA"
                    OtId = ManiaOtId }
            Some(party @ [ shuckie ])

    /// Source SHUCKIE_* return code and updated party.
    let returnToMania partyIndex (party: Party) : int * Party =
        if partyIndex < 0 || partyIndex >= party.Length then 1, party
        else
            let mon = party.[partyIndex]
            if mon.SpeciesId <> 213 || mon.OtId <> ManiaOtId || mon.OtName <> "MANIA" then 0, party
            elif mon.Hp <= 0 then 4, party
            elif mon.Friendship >= 150 then 3, party
            else
                2,
                (party
                 |> List.mapi (fun i candidate -> i, candidate)
                 |> List.choose (fun (i, candidate) -> if i = partyIndex then None else Some candidate))
