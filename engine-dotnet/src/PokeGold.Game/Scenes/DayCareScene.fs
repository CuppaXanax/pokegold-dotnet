namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player

type DayCareScene(content: Content, initialPlayer: PlayerState, mode: string, onPlayer: PlayerState -> unit, onResult: int option -> unit) =
    let mutable player = initialPlayer
    let mutable continuation: (unit -> Transition) option = None
    let mutable yesNoResult = 0
    let mutable selectedPartyIndex: int option = None

    let text message =
        TextBoxScene.Of(content, message + "<DONE>") :> Scene

    let show message next =
        continuation <- Some next
        Push(text message)

    let ask message next =
        continuation <- Some next
        Push(YesNoScene(content.Font, fun result -> yesNoResult <- result) :> Scene)

    let finish result =
        onPlayer player
        onResult result
        Pop

    let isMan = mode = "MAN"

    let slot () =
        if isMan then player.DayCare.Mon1 else player.DayCare.Mon2

    let updateDayCareSlot mon dayCare =
        if isMan then { dayCare with Mon1 = mon } else { dayCare with Mon2 = mon }

    let initBreeding dayCare =
        match dayCare.Mon1, dayCare.Mon2 with
        | Some a, Some b when Breeding.compatible a b ->
            { dayCare with HasEgg = false; EggSteps = 256 }
        | _ ->
            { dayCare with HasEgg = false; EggSteps = 0 }

    let hasOtherUsable idx =
        player.Party
        |> List.indexed
        |> List.exists (fun (i, mon) -> i <> idx && not (Breeding.isEgg mon) && mon.Hp > 0)

    let depositSelected idx =
        let mon = List.item idx player.Party

        if Breeding.isEgg mon then
            show "We can't raise an EGG." (fun () -> show "Come again." (fun () -> finish None))
        elif not (hasOtherUsable idx) then
            show "That's your last healthy #MON." (fun () -> show "Come again." (fun () -> finish None))
        else
            let party =
                player.Party
                |> List.indexed
                |> List.choose (fun (i, m) -> if i = idx then None else Some m)

            let dayCare =
                player.DayCare
                |> updateDayCareSlot (Some mon)
                |> initBreeding

            player <- { player with Party = party; DayCare = dayCare }
            show "All right. I'll raise your #MON." (fun () -> show "Come back later." (fun () -> finish None))

    let processPartyPick () =
        match selectedPartyIndex with
        | Some idx -> depositSelected idx
        | None -> show "Oh, fine then." (fun () -> show "Come again." (fun () -> finish None))

    let pickParty () =
        selectedPartyIndex <- None
        continuation <- Some processPartyPick
        Push(
            PartyScene(
                content,
                player,
                (fun p -> player <- p),
                onSelect = (fun idx ->
                    selectedPartyIndex <- Some idx
                    Pop)) :> Scene)

    let startDeposit () =
        if player.Party.Length < 2 then
            show "You only have one #MON." (fun () -> show "Come again." (fun () -> finish None))
        else
            show "Which one should I raise?" pickParty

    let withdrawExisting mon =
        let price = 100
        show (sprintf "It will cost ¥%d to get your #MON back." price) (fun () ->
            ask "" (fun () ->
                if yesNoResult = 0 then
                    show "Oh, fine then." (fun () -> show "Come again." (fun () -> finish None))
                elif player.Party.Length >= 6 then
                    show "You have no room for it." (fun () -> show "Come again." (fun () -> finish None))
                elif not (Money.canAfford player.Money price) then
                    show "You don't have enough money." (fun () -> show "Come again." (fun () -> finish None))
                else
                    let dayCare =
                        player.DayCare
                        |> updateDayCareSlot None
                        |> initBreeding

                    player <-
                        { player with
                            Money = Money.take player.Money price
                            Party = player.Party @ [ mon ]
                            DayCare = dayCare }

                    show "Perfect! Here's your #MON." (fun () -> show "Got back your #MON." (fun () -> finish None))))

    let startResident () =
        match slot () with
        | Some mon -> withdrawExisting mon
        | None ->
            let who = if isMan then "I'm the DAY-CARE MAN." else "I'm the DAY-CARE LADY."
            show who (fun () ->
                ask "" (fun () ->
                    if yesNoResult = 0 then
                        show "Oh, fine then." (fun () -> show "Come again." (fun () -> finish None))
                    else
                        startDeposit ()))

    let giveEgg () =
        match player.DayCare.Mon1, player.DayCare.Mon2 with
        | Some a, Some b ->
            let egg = Breeding.generateEgg a b
            let dayCare = { player.DayCare with HasEgg = false; EggSteps = 256 }
            player <- { player with Party = player.Party @ [ egg ]; DayCare = dayCare }
            show "Received the EGG!" (fun () -> show "Take good care of it." (fun () -> finish (Some 0)))
        | _ ->
            player <- { player with DayCare = { player.DayCare with HasEgg = false; EggSteps = 0 } }
            show "I haven't found an EGG yet." (fun () -> finish (Some 0))

    let startOutside () =
        if not player.DayCare.HasEgg then
            show "Not yet..." (fun () -> finish (Some 0))
        else
            show "We found an EGG!" (fun () ->
                ask "" (fun () ->
                    if yesNoResult = 0 then
                        player <- { player with DayCare = { player.DayCare with HasEgg = false; EggSteps = 0 } }
                        show "I'll keep it. Thanks!" (fun () -> finish (Some 0))
                    elif player.Party.Length >= 6 then
                        show "You have no room for the EGG." (fun () -> finish (Some 1))
                    else
                        giveEgg ()))

    let start () =
        match mode with
        | "MAN"
        | "LADY" -> startResident ()
        | "OUTSIDE" -> startOutside ()
        | _ -> finish None

    interface Scene with
        member _.Update(_buttons: Buttons) : Transition =
            match continuation with
            | Some next ->
                continuation <- None
                next ()
            | None -> start ()

        member _.Render(_fb: Framebuffer) = ()
