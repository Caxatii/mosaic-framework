# Mosaic Design Guide

Decisions you make every day, and how to make them so the architecture stays healthy.

This guide is for everyone on the team, and especially for people new to Mosaic or to game architecture in general.
You do not need to read it in one go. Read the **cheat sheet** first, then come back to a section when you meet
that situation in your code or in a review.

Each case follows the same pattern:

- **Situation**: what you are trying to do.
- **Don't**: the tempting way that causes problems.
- **What goes wrong**: the consequences, often weeks later.
- **Do**: the way that fits the architecture.
- **Why**: the idea behind the rule, so you can apply it to cases this guide does not cover.

Examples come from a top-down arena shooter (health, weapons, loot). Your game will have other features, but the
same decisions come up.

**Contents**

- [Cheat sheet](#cheat-sheet) · [Where does my code go?](#where-does-my-code-go) · [Glossary](#glossary)
- Part 1. Changing the game state: wrong owner, another feature's data, events from the wrong place, hidden changes
- Part 2. Commands and events: input that skips commands, objects in messages, rejection reasons, disguised orders,
  event payloads
- Part 3. Entities, components and content: keeping ids, one big component, flag or component, reading a change
  back, changing content at runtime, using an entity after it was destroyed
- Part 4. Connecting features: direct call or Reaction, singletons, tick order, subscriptions, registering systems
- Part 5. The domain and Unity: rules in MonoBehaviours, Unity time and random, Queries and snapshots, events or
  polling
- Part 6. Game flow, screens and windows: state changes from subscribers, screens that decide, late clicks,
  who starts the match, pause, the match in a static field, saving, window policy, enter and exit, garbage
- Part 7. Testing and debugging habits
- Part 8. Naming
- Part 9. Code review checklist
- Part 10. Symptoms and likely causes

---

## Cheat sheet

If you remember only this page, you will avoid most problems.

1. **Only Operations change components.** Everything else reads.
2. **The code that made a change publishes the event about it.** That is always Operations.
3. **Everything that comes from outside the simulation is a command.** Player input, AI decisions, cheats, network.
4. **Presentation never changes the game.** It reads Queries, listens to events and sends commands.
5. **Commands and events are small `readonly struct`s** with ids and numbers, never objects.
6. **Keep `EntityId`, not `Entity` or components,** in anything that lives longer than one method.
7. **Content (definitions, configs) is read-only at runtime.** Copy the value into a component if it must change.
8. **No `UnityEngine` in the domain.** No `Time.time`, no `UnityEngine.Random`, no `MonoBehaviour`.
9. **Dependencies come through constructors.** No singletons, no static "managers", no service locator.
10. **If a feature is an optional consequence of another, connect them with a Reaction.**
11. **Adding or removing a component becomes visible after the action ends; destroying an entity takes effect at
    once.** Don't read a component back right after changing it, and don't use an entity after destroying it.
12. **Every subscription has an owner that disposes it.**

---

## Where does my code go?

Ask these questions in order. Stop at the first "yes".

| Question | Put it in |
|---|---|
| Is it data about one aspect of an entity (health, ammo, speed)? | a **Model** component (`IModel`) |
| Is it a value designers set in the editor? | a **Definition** (`IEntityPart`) inside a config asset |
| Does it change components or spawn/destroy entities? | **Operations** of the feature that owns those components |
| Does a player, AI or tool ask the game to do something? | a **Command** + **CommandHandler** |
| Does it happen by itself over time (timers, movement, cooldowns)? | an **EntitySystem** |
| Is it "when X happens in feature A, do Y in feature B"? | a **Reaction** |
| Is it a calculation other rules need (distance, is alive, damage after armor)? | an **Evaluator** |
| Does UI, saving or another outside system need to read it? | **Queries** returning a snapshot |
| Is it about how things look or sound? | **Presentation** (presenter + view) |

If you cannot answer, the code probably does two things. Split it.

---

## Glossary

**Entity**: an id for a "thing" in the game: the player, an enemy, a bullet, an item. An entity has no logic and no
data of its own; it is just a number (`EntityId`) that components are attached to.

**Component (Model)**: a small class with data about one aspect of an entity, for example `Health` or `Weapon`.
It protects its own rules (health never goes below zero) but knows nothing about the rest of the game.

**Definition**: the designer-facing description of a component, stored in a config asset. It creates the component
when an entity spawns.

**Action**: one unit of work in the simulation: handling one command, or one tick of one system. Changes made during
an action become visible to everyone when the action ends.

**Command**: a request from outside: "fire in this direction", "use item in slot 2". It can be accepted or rejected.

**Event**: a fact that already happened: "weapon fired", "entity died". It cannot be rejected; it can only be
reacted to.

**Operations**: the class of a feature that is allowed to change that feature's components.

**Evaluator**: a class with calculations over the current state. It never changes anything.

**Queries**: the read-only window into a feature for code outside the domain (UI, saving).

**System**: logic that runs every tick for entities with certain components.

**Reaction**: a rule that listens to an event of one feature and changes another feature through its Operations.

**Composition root**: the one place where all of these objects are created and connected (for example, the match class).

**ECS (Entity Component System)**: an architecture where game objects are ids made of components and logic runs
in systems. Mosaic uses these ideas but adds strict roles (Operations, commands, events) on top. If you come from
DOTS, Entitas or LeoECS, read [Architecture, section 2](Architecture.md#2-mosaic-and-ecs): several habits from
those frameworks (any system writes any component, input components, polling for changes) are exactly what this
guide warns against, and the section explains why.

**Domain**: all of the above. The part of the game that does not depend on Unity.

**Presentation**: everything the player sees and hears: views, presenters, UI. It depends on the domain, never the
other way around.

---

# Part 1. Changing the game state

## 1.1 Changing a component from outside its Operations

**Situation.** A health potion is picked up in the UI and you want to heal the player.

**Don't.**

```csharp
// InventoryPresenter.cs (presentation)
var health = world.Get<Health>(player);
health.Current += 40; // or health.Restore(40) made public "just for this"
```

**What goes wrong.**
- Nobody publishes `Healed`, so the health bar, the heal sound and the "healed 40" popup never show up.
- The rule "a dead player cannot be healed" lives in Operations. This code skips it, and now dead players can be
  healed from the inventory.
- The match recording does not see this change, so a replay of this match goes differently.
- Next month someone searches for "who changes health?" and finds three places instead of one.

**Do.** Send a command. The handler checks the rules, Operations make the change and announce it.

```csharp
// presentation
commands.Send(new UseItemCommand(player, slot));

// domain: the handler decides, Operations change
private CommandResult UseItem(Entity user, UseItemCommand command)
{
    if (!_combatEvaluator.IsAlive(user))
        return CommandResult.Rejected(UseItemError.UserDead);

    if (_inventoryEvaluator.ItemIn(user, command.Slot)?.Get<HealOnUse>() is not { } potion)
        return CommandResult.Rejected(UseItemError.NotUsable);

    _healthOperations.Heal(user, potion.Amount, source: user.Id); // publishes Healed
    return CommandResult.Ok;
}
```

**Why.** When exactly one place can change a component, that place can guarantee its rules and its events.
Everything else in the game can trust them.

> Rule of thumb: component methods that change data are `internal`. If you feel the need to make one `public`,
> you are about to change it from the wrong place.

## 1.2 Changing another feature's components

**Situation.** When an enemy spawns on a hard difficulty, its health should be doubled. Spawning lives in the
Arena feature, health belongs to Combat.

**Don't.**

```csharp
// ArenaOperations.cs
var enemy = _world.Spawn(definition);
enemy.Get<Health>().Scale(2f); // Arena reaches into Combat's component
```

**What goes wrong.** Combat no longer knows everything that happens to health. When someone adds "max health
changed" events or a health cap, the Arena code silently bypasses them. Bugs like "enemy has 200/100 HP" appear and
nobody in Combat can find the cause, because it is not in Combat.

**Do.** Ask the owner to do it.

```csharp
// ArenaOperations.cs
var enemy = _world.Spawn(definition);
_healthOperations.ScaleMax(enemy, _definition.EnemyHealthMultiplier); // owned by Combat
```

**Why.** Each feature is the only author of its data. Other features may ask, but never write.

## 1.3 Publishing an event from the wrong place

**Situation.** The attack command was accepted and you want the UI to show a muzzle flash.

**Don't.**

```csharp
// CombatCommandHandler.cs
_weaponOperations.Fire(attacker, weapon, command.Direction);
_events.Publish(new WeaponFired(attacker.Id)); // the handler announces the fact
```

**What goes wrong.**
- `Fire` can fail (no ammo after a reload race). The event is published anyway, and the UI flashes for a shot that
  never happened.
- AI shoots through a different path that calls `Fire` directly. AI shots never produce the event, so enemies fire
  without a flash.
- Two places now publish the same fact, so sooner or later it gets published twice.

**Do.** Publish the event inside Operations, right after the change succeeded.

```csharp
// WeaponOperations.cs
public Entity? Fire(Entity shooter, Entity weapon, Vector2 direction)
{
    if (!weapon.Get<Weapon>().TryConsume())
        return null;                                   // nothing happened, nothing to announce

    var projectile = SpawnProjectile(shooter, direction);
    _events.Publish(new WeaponFired(shooter.Id, projectile.Id));
    return projectile;
}
```

**Why.** An event is a promise: "this really happened". Only the code that made it happen can keep that promise.
That is why `CommandHandler` and `Reaction` do not even have access to the event bus.

## 1.4 Hidden changes in "read-only" code

**Situation.** You need the nearest enemy, and enemies should only be targeted by one shooter at a time.

**Don't.**

```csharp
// CombatEvaluator.cs
public Entity? FindTarget(Entity seeker)
{
    var target = FindNearestHostile(seeker);
    target?.Get<Targeted>().MarkBy(seeker.Id); // a "find" method that also changes state
    return target;
}
```

**What goes wrong.** Somebody calls `FindTarget` from a debug overlay "just to look", and enemies get locked as
targeted. A HUD calls it every frame to draw an arrow, and targeting breaks completely. Bugs that appear only
when a window is open are among the hardest to find.

**Do.** Split reading from changing.

```csharp
var target = _combatEvaluator.FindNearestUntargeted(seeker); // reads only
if (target is { } t)
    _targetingOperations.Lock(t, seeker);                     // changes and announces
```

**Why.** Evaluators and Queries are safe to call any number of times, from anywhere, in any order. That safety
disappears the moment one of them changes something. `ArchitectureRules` from `Mosaic.Testing` checks this rule
automatically once your game has an architecture test (see *7.5*).

---

# Part 2. Commands and events

## 2.1 Input that skips commands

**Situation.** The reload key should reload the weapon. Calling the Operations directly looks shorter.

**Don't.**

```csharp
// PlayerInputPresenter.cs
if (keyboard.rKey.wasPressedThisFrame)
    match.WeaponOperations.StartReload(weapon); // made public "for the input"
```

**What goes wrong.**
- The checks live in the handler (dead player, full magazine, already reloading). The input skips them, so a dead
  player can reload.
- AI reloads through a command and the player does not. Two paths for one action drift apart with every change.
- Match recording only sees commands. A bug report replay no longer matches what the tester saw.
- Operations had to become `public`, so any presenter can now call them.

**Do.**

```csharp
if (keyboard.rKey.wasPressedThisFrame)
    _commands.Send(new ReloadCommand(player));
```

**Why.** A command is the single entrance into the simulation. Everyone who wants something (player, AI, cheat
console, network) goes through the same door and the same checks.

## 2.2 Putting objects into commands and events

**Situation.** The use-item command needs the item, so you pass the item object.

**Don't.**

```csharp
public readonly struct UseItemCommand : ICommand
{
    public UseItemCommand(Entity user, ItemDefinition item) { User = user; Item = item; }

    public Entity User { get; }           // carries a World reference
    public ItemDefinition Item { get; }   // a content object
}

public class DamageEvent : IEvent { ... } // a class: one allocation per hit
```

**What goes wrong.**
- The command cannot be recorded or sent over the network: what would "an `ItemDefinition` object" look like in a
  file? Replay tests stop working the day this command is added (`ReplayFormat` rejects it).
- `Entity` holds a reference to a `World`. Send the command to another world (a replay, a server) and it points at
  the wrong one.
- A class event allocates memory on every hit. With hundreds of bullets that is garbage collection spikes.

**Do.** Small `readonly struct`s with ids and numbers.

```csharp
public readonly struct UseItemCommand : IEntityCommand
{
    EntityId IEntityCommand.Entity => User;

    public UseItemCommand(EntityId user, int slot) { User = user; Slot = slot; }

    public EntityId User { get; }
    public int Slot { get; }
}
```

**Why.** Commands and events are messages. A message should make sense when written to a file, sent over a network
or read a minute later. Ids, numbers, enums and strings pass that test. References do not.

## 2.3 Rejecting with `bool` or `string`

**Situation.** An attack can fail for several reasons and the UI should explain why.

**Don't.**

```csharp
public bool Attack(...)          // the caller learns nothing
public string Attack(...)        // "no ammo", "No ammo", "out of ammo"... compared with ==
```

**What goes wrong.** With `bool` the UI cannot tell "reloading" from "no weapon" and shows the same message.
With strings, a typo or a translation breaks the comparison, and nobody notices until a player reports it.

**Do.** An enum per feature, returned through `CommandResult`.

```csharp
public enum AttackError { AttackerDead, NoWeapon, Reloading, Cooldown, NoAmmo }

return CommandResult.Rejected(AttackError.Reloading);

// presentation
if (result.Reason is AttackError.NoAmmo)
    _hud.FlashAmmoCounter();
```

**Why.** An enum is a list of every way the action can fail, checked by the compiler. The UI can handle each case,
and adding a new reason shows up in a code review instead of in production.

## 2.4 Events that are orders in disguise

**Situation.** When an enemy dies, a sound should play and the score should increase.

**Don't.**

```csharp
_events.Publish(new PlayDeathSound(enemy.Id));
_events.Publish(new AddScore(100));
```

**What goes wrong.** Combat now knows about sounds and scoring. Every new consequence of a death (achievements,
statistics, a slow-motion effect) means editing Combat again. And if nobody subscribes to `AddScore`, the score is
silently lost: an order without anyone to carry it out.

**Do.** Publish what happened, in the past tense. Let others decide what it means to them.

```csharp
_events.Publish(new EntityDied(enemy.Id, killer: attacker.Id));

// Presentation plays a sound. A ScoreReaction adds points through ScoreOperations.
```

**Why.** An event describes the past, so its author does not need to know who listens. Name events as facts:
`EntityDied`, `WeaponFired`, `ItemPickedUp`. If the name sounds like an instruction (`PlaySound`, `AddScore`,
`DoDamage`), it is either a command or a direct call to Operations.

## 2.5 Events without the numbers listeners need

**Situation.** The health bar should update when the player takes damage.

**Don't.**

```csharp
_events.Publish(new DamageApplied(target.Id)); // "something happened, go look"
```

```csharp
// presenter
void OnDamage(DamageApplied e) => _bar.Set(_combatQueries.StatusOf(e.Target).Health);
```

**What goes wrong.** By the time the presenter runs, more may have happened in the same action: a heal, a second
hit, death. The bar shows the final number and the "-25" popup cannot be drawn at all, because the event never said
how much damage it was.

**Do.** Put the values of that moment into the event.

```csharp
_events.Publish(new DamageApplied(target.Id, source, dealt, healthAfter: health.Current, maxHealth: health.Max));
```

**Why.** An event is a snapshot of one moment. Listeners should be able to react using only the event, without
asking the world what it looks like now.

---

# Part 3. Entities, components and content

## 3.1 Keeping entities and components in fields

**Situation.** A homing missile needs to remember its target between ticks.

**Don't.**

```csharp
public sealed class Homing : IModel
{
    public Health TargetHealth;   // a component of another entity
    public Entity Target;         // a handle bound to one World
}
```

**What goes wrong.** The target dies and is destroyed. Its slot is reused by a new entity, maybe a pickup. The
missile still holds the old `Health` object and keeps "chasing" something that no longer exists, or worse, holds
an `Entity` whose slot now belongs to a potion and flies at the potion.

**Do.** Store the `EntityId` and check it every time you use it.

```csharp
public sealed class Homing : IModel
{
    public Homing(EntityId target) => Target = target;
    public EntityId Target { get; }
}

// in the system
if (!world.TryGetEntity(homing.Target, out var target))
{
    _operations.Detonate(missile);   // the target is gone: decide what that means
    return;
}
```

**Why.** An `EntityId` contains a generation number. When a slot is reused, the generation changes, so an old id
can never be mistaken for the new entity. `Entity` and components are fine inside one method; anything that lives
longer (fields, components, commands, saved data) keeps ids.

## 3.2 One big component for everything

**Situation.** The player needs health, ammo, speed, inventory and a score.

**Don't.**

```csharp
public sealed class PlayerData : IModel
{
    public float Health, MaxHealth, Speed;
    public int Ammo, Score;
    public List<Item> Inventory;
}
```

**What goes wrong.** Enemies need health and speed but not a score, so you write `EnemyData` with the same fields
copied. Damage code now handles both. A turret needs health but not speed: a third copy. Every feature edits the
same class, and every change risks breaking someone else's.

**Do.** One component per aspect, and entities made of the aspects they need.

```csharp
player: Health, Movement, Weapon slot, Backpack, Score
enemy:  Health, Movement, Weapon slot, Brain
turret: Health, Weapon slot
crate:  Health, LootDrop
```

**Why.** Damage works on anything with `Health`. Movement moves anything with `Movement`. A new kind of entity is a
new combination in a config asset, often with no new code at all.

## 3.3 A flag field or a component?

**Situation.** An entity can be stunned for two seconds.

**Option A, a field:** `Movement.IsStunned`.
**Option B, a component:** add `Stunned` when stunned, remove it when it ends.

Both are fine. Choose with this table:

| Prefer a field when... | Prefer a component when... |
|---|---|
| the value changes very often (every few ticks) | the state comes and goes rarely |
| it belongs to one feature only | several features care ("can't shoot", "can't move", "show stars") |
| it has no data of its own | it has its own data (time left, source) |

**Watch out for.** Adding and removing components is a structural change and is buffered until the end of the
action (see 3.4). Turning something on and off many times per action is easier with a field.

**Why.** A component is visible to queries and systems: `Query<Stunned>()` finds all stunned entities, and a
`StunSystem` ticks only them. A field is cheaper but every reader has to know about it.

## 3.4 Reading a change back in the same action

**Situation.** The poison should end and, if it ended, show "cured".

**Don't.**

```csharp
entity.Remove<Poisoned>();
if (!entity.Has<Poisoned>())        // still true here!
    _events.Publish(new Cured(entity.Id));
```

**What goes wrong.** Inside an action, adding and removing components on an existing entity is buffered. `Has`
still sees the poison, so `Cured` is never published. In the editor you also get a stale-read warning in the
console; in tests (strict mode) this line throws.

**Do.** Use what you already know instead of asking again.

```csharp
if (entity.Has<Poisoned>())
{
    entity.Remove<Poisoned>();
    _events.Publish(new Cured(entity.Id));
}
```

**Why.** The buffer is what makes iteration safe: systems can remove components from entities while looping over
them. The price is that changes show up when the action ends. If you need the new state, you already have it in a
local variable, or you react to the event in a subscriber, which runs after the buffer is applied.

> Two exceptions. A newly spawned entity sees its own components through `Get` right away, because Operations need
> to finish building it (queries still see it only after the action). And `Destroy` takes effect at once for
> everyone, see 3.6.

## 3.5 Changing content at runtime

**Situation.** A "rage" buff should make the player's weapon fire faster.

**Don't.**

```csharp
var weapon = player.Get<Weapon>();
weapon.Definition.FireInterval *= 0.5f; // the definition comes from a config asset
```

**What goes wrong.** Every weapon of that type shares the same definition object. Now **every** enemy with that
gun fires twice as fast, forever, until you restart. In the editor it is worse: the change is written into the
asset, and the next commit contains a "balance change" nobody made on purpose.

**Do.** Keep the current value in the component and let Operations change it. Or compute it from the definition
plus modifiers when you need it.

```csharp
var interval = weapon.Definition.FireInterval * _buffEvaluator.FireRateMultiplier(player); // read-only
```

**Why.** Definitions describe "what this kind of thing is". Components describe "how this particular one is doing
right now". Mixing them means one entity's state leaks into all of them.

## 3.6 Using an entity after it was destroyed

**Situation.** Two players touch the same coin in the same frame, or a reaction runs for an enemy that another
reaction has already removed.

**Don't.** Protect every Operations with its own flag:

```csharp
public sealed class Coin : IModel
{
    public bool IsCollected { get; private set; }   // "just in case it gets collected twice"
}
```

**What goes wrong.** Every feature invents its own flag (`IsCollected`, `IsDead`, `IsConsumed`), and every
Evaluator and system must remember to check it. The one place that forgets pays a coin twice or heals a corpse.

**What Mosaic does instead.** `Destroy` makes the entity unavailable for everyone at once: `IsAlive` is false,
`Get` returns `null`, queries and systems skip it and commands to it are rejected. The second player's search
doesn't find the coin. If code still holds the destroyed entity and uses it, it gets an `EntityNotAliveException`
pointing at the exact line, instead of a silent double payment.

**Do.** Read and check everything first, change after. Then a mistaken second call fails before it changes anything:

```csharp
public void Collect(Entity collector, Entity coin)
{
    var wallet = collector.Get<Wallet>();
    var value = coin.Get<Coin>().Value;   // a destroyed coin fails here, before anything changed

    wallet.Add(value);
    coin.Destroy();
    _events.Publish(new CoinCollected(collector.Id, value, wallet.Coins));
}
```

If `wallet.Add` came first, a second call would pay and only then fail, leaving the game half-changed.

Check `IsAlive` (or use `TryGetEntity`) only where an entity may **legitimately** be gone, for example an id kept
from an earlier frame or an event about an entity another reaction may have removed:

```csharp
if (!World.TryGetEntity(e.Target, out var target))
    return;   // already removed by someone else: nothing to do
```

**Why.** Code should not need to know about every other piece of code that might destroy the same entity. The world
guarantees that a destroyed entity can't be used, and a bug that tries anyway is loud and easy to find.


## 3.7 Timers and delays

**Situation.** An ability has a cooldown, a grenade explodes three seconds after it is thrown.

**Don't.** Write the same `TimeLeft -= deltaTime` in every model with its own rounding, or schedule a callback
(`Delay(3, () => Explode(grenade))`).

**What goes wrong.** Hand-written timers go negative, report "finished" twice or never, and a repeating one loses the
overshoot every step. A callback can't be saved, replayed or seen in the World inspector, and it keeps running after
the grenade is gone.

**Do.** Keep a `Countdown` in the model and let a system advance it:

```csharp
public sealed class Fuse : IModel
{
    private Countdown _left;
    public Fuse(float seconds) => _left = Countdown.Start(seconds);
    internal bool Burn(float deltaTime)
    {
        _left = _left.Advance(deltaTime, out var exploded);
        return exploded;
    }
}

internal sealed class FuseSystem : EntitySystem<Fuse>
{
    protected override void Tick(Entity grenade, Fuse fuse, float deltaTime)
    {
        if (_grenades.Burn(grenade, deltaTime))       // Operations: changes the fuse, explodes, publishes
            _grenades.Explode(grenade);
    }
}
```

**Why.** The delay is state like any other: saved, replayed, paused with the game, gone with its entity.

---

# Part 4. Connecting features

## 4.1 Direct call or Reaction?

**Situation.** Two features need to work together. When should one call the other, and when should it use a Reaction?

Ask one question: **does feature A still make sense without feature B?**

| Example | A works without B? | Connect with |
|---|---|---|
| Weapon spawns a projectile and moves it | no: a weapon that cannot move projectiles is broken | direct call to `MovementOperations` |
| Damage takes armor into account | no: armor is part of the damage rule | direct call to `ArmorEvaluator` |
| An enemy dies, loot drops | yes: combat works fine without loot | `Reaction` in the Loot feature |
| An enemy dies, the score goes up | yes | `Reaction` in the Score feature |
| A wave ends when all enemies are dead | yes: combat doesn't care about waves | `Reaction` (or a system) in the Arena feature |

**Don't.** Make Combat call loot, score, achievements and statistics directly from `ApplyDamage`.

**What goes wrong.** Combat's constructor grows with every new feature. You cannot test combat without building
loot, score and achievements. Removing achievements for a demo build means editing combat code.

**Do.**

```csharp
internal sealed class DropLootReaction : Reaction
{
    private readonly InventoryOperations _inventoryOperations;

    public DropLootReaction(GameplayContext context, InventoryOperations inventoryOperations) : base(context)
    {
        _inventoryOperations = inventoryOperations;
        On<EntityDied>(OnDied);
    }

    private void OnDied(EntityDied e)
    {
        if (World.Get<LootDrop>(e.Entity) is { } loot)
            _inventoryOperations.SpawnLoot(loot, at: World.Get<Position>(e.Entity).Value);
    }
}
```

**Why.** Dependencies point from the optional feature to the core one. The core never changes when optional
features are added or removed. Delete the reaction and the game still runs, just without loot.

## 4.2 Singletons and static managers

**Situation.** Many classes need the item catalog. A static instance is one line away.

**Don't.**

```csharp
public static class GameManager
{
    public static ItemCatalog Items;
    public static World World;
}

// anywhere
var item = GameManager.Items[id];
```

**What goes wrong.**
- Tests run one after another in the same process. Test B sees the world left over from test A and fails only when
  run together. These "flaky" tests waste hours.
- A replay builds a second match next to the real one. Both now share one static `World`.
- You cannot tell what a class needs by looking at it. Moving code between features becomes a hunt for hidden
  `GameManager.X` lines.

**Do.** Pass dependencies through the constructor and create everything in the composition root.

```csharp
internal sealed class InventoryOperations
{
    public InventoryOperations(World world, IEventBus events, ItemCatalog items) { ... }
}
```

**Why.** A constructor is an honest list of what a class needs. Two matches, a replay and a test can exist side by
side because nothing is shared by accident. And if a constructor gets too long, it is telling you the class does
too much.

## 4.3 Systems that depend on tick order by accident

**Situation.** Projectiles stopped hitting fast enemies after a refactor.

**What happened.** The tick order used to be `Movement → Projectiles`: projectiles moved, then checked hits. A new
system list put `Projectiles` first, so hits were checked against positions from the previous tick and fast
targets were already gone.

**Don't.** Register systems from several places, or rely on "the order in which features were added".

**Do.** Keep the tick order in one list, with a one-line reason for each position.

```csharp
_systems = new ISystem[]
{
    brains,        // decide: AI sends commands for this tick
    weapons,       // timers: cooldowns and reloads
    buffs,         // modifiers before anything uses them
    movement,      // move
    projectiles,   // hits, after movement so they see new positions
    corpses,       // clean up the dead
    waves,         // spawn the next wave last
};
```

**Why.** Order is part of the game rules. When it is written down in one place, a change to it is visible in a code
review instead of in a bug report.

## 4.4 Subscriptions nobody disposes

**Situation.** A presenter subscribes to `DamageApplied` to flash the screen.

**Don't.**

```csharp
public HitFlashPresenter(IEventBus events)
{
    events.Subscribe<DamageApplied>(Flash); // the IDisposable is thrown away
}
```

**What goes wrong.** The player leaves the match and starts a new one. The old presenter is gone from the screen
but still subscribed: it flashes a destroyed view (`MissingReferenceException`) or, if it outlives the match,
keeps the whole old world in memory.

**Do.** Keep the subscription and dispose it when the owner goes away.

```csharp
public sealed class HitFlashPresenter : IDisposable
{
    private readonly IDisposable _subscription;

    public HitFlashPresenter(IEventBus events) =>
        _subscription = events.Subscribe<DamageApplied>(Flash);

    public void Dispose() => _subscription.Dispose();
}
```

In the domain, `Reaction.On` keeps subscriptions for you and `Reaction.Dispose` releases them all.

**Why.** Every subscription is a reference from the bus to your object. If nobody removes it, your object lives as
long as the bus does.

## 4.5 Registering a system so it ticks

**Situation.** You wrote a `RegenerationSystem`. It compiles, but nothing regenerates.

Systems do not tick by themselves. Something has to call them every frame, in the right order, inside a simulation
action. In Mosaic that "something" is the composition root (the match class), and it does it in three steps.

**Step 1. Create the system in the composition root,** after everything it depends on:

```csharp
// Match constructor
HealthOperations = new HealthOperations(events, armor, buffs, items);
var regeneration = new RegenerationSystem(Context, HealthOperations);
```

**Step 2. Put it into the tick list,** at the position where it makes sense (see 4.3):

```csharp
_systems = new ISystem[]
{
    brains,
    weapons,
    buffs,
    regeneration,   // after buffs: a "no regen" debuff must already be applied
    movement,
    projectiles,
    corpses,
    waves,
};
```

**Step 3. Tick the list through `Simulation`,** once per frame, from one place:

```csharp
// Match.Tick, called by the gameplay state every frame (and not during pause)
public void Tick(float deltaTime)
{
    if (deltaTime <= 0)
        return;

    Context.Commands.Recording?.RecordTick(deltaTime);
    foreach (var system in _systems)
        Context.Simulation.Tick(system, deltaTime);   // each system tick is one action
}
```

Step 3 is written once per game. After that, adding a system is steps 1 and 2.

**How each role gets connected.** Systems are the only role that must be added to a list. The others connect
themselves when they are constructed:

| Role | How it is connected | What happens if you forget |
|---|---|---|
| System | created **and** added to the tick list | never runs, no error |
| CommandHandler | created; its constructor calls `On<TCommand>(...)` | `Send` throws "has no handler" |
| Reaction | created; its constructor calls `On<TEvent>(...)`; keep it if you need to `Dispose` it | the consequence never happens, no error |
| Operations, Evaluator | created and passed to whoever needs them | does not compile |
| Queries | created and exposed as a property of the match | presentation cannot read the feature |

The two silent ones, systems and reactions, are worth a test: "after one second the entity has regenerated",
"after death the loot is on the ground".

**Don't.**

```csharp
// 1. Calling the system directly
regeneration.Tick(deltaTime);
```

The system runs outside a simulation action: structural changes are not buffered (iteration throws
`InvalidOperationException`), and events published during the tick are delivered only with the next action.

```csharp
// 2. Ticking from a MonoBehaviour
void Update() => _regeneration.Tick(Time.deltaTime);
```

The system keeps running during pause, its order relative to other systems depends on Unity's script execution
order, and tests and replays never tick it.

```csharp
// 3. A system that registers itself
public RegenerationSystem(GameplayContext context, ...) : base(context) => Systems.All.Add(this);
```

The order now depends on the order constructors happen to run in, and there is a static list again (see 4.2).

**Why.** One list, one place, one way of ticking: anyone can open the composition root and see every system the
game runs and in what order. A system that is not in the list does not run, which is easy to spot in a review.


## 4.6 A part that another feature has to serve

**Situation.** A buff gets a new part, "freeze". The buff feature applies and removes buffs; what freezing does
belongs to Spatial.

**Don't.** Teach the buff feature about freezing (`if (part is Freeze) movement.Stop(...)`), or register the handler
somewhere and hope nobody forgets it for the next part.

**What goes wrong.** In the first case Buffs depends on every feature its parts touch. In the second a part
without a handler does nothing, silently: the frost potion is in the game and does not work, and only a player
notices.

**Do.** Mark such parts with an interface, let the owning feature write the handler, register it in the composition
root and check the content when the match is built.

```csharp
public interface IBuffEffectPart : IBuffPart { }
public sealed class Freeze : IBuffEffectPart { public float Seconds; }

internal sealed class FreezeEffect : BuffEffect<Freeze> { ... }       // Spatial, calls MovementOperations

buffEffects.Register(new FreezeEffect(movementOperations));
var missing = buffs.SelectMany(b => buffEffects.Unhandled<IBuffEffectPart>(b.Effects, b.Name)).ToList();
```

**Why.** `PartHandlers` keeps one handler per part type and finds the parts nobody serves; the match does not build
until every one of them has a handler. Buffs stays unaware of freezing, as Combat stays unaware of loot (4.1).

---

# Part 5. The domain and Unity

## 5.1 Game rules in a MonoBehaviour

**Situation.** An enemy should regenerate health while out of combat. The enemy prefab already has a script.

**Don't.**

```csharp
public class EnemyView : MonoBehaviour
{
    void Update()
    {
        if (Time.time - _lastHit > 5f)
            _health.Restore(2f * Time.deltaTime);
    }
}
```

**What goes wrong.**
- Pause stops the simulation, but not `Update`: enemies heal during the pause menu.
- Tests run without scenes and prefabs, so this rule can never be tested.
- Turn off the enemy's renderer for a performance trick, and the enemy stops regenerating.
- A server build without graphics has no prefabs, so regeneration simply does not exist there.

**Do.** A component for the data and a system for the rule.

```csharp
public sealed class Regeneration : IModel { ... }   // rate, delay, time since last hit

internal sealed class RegenerationSystem : EntitySystem<Regeneration, Health>
{
    protected override void Tick(Entity entity, Regeneration regen, Health health, float deltaTime) =>
        _healthOperations.Regenerate(entity, regen, deltaTime);
}
```

The view only shows the result, for example by listening to `Healed`.

**Why.** If a rule lives in the domain, it runs the same way in the game, in tests, in a replay and on a server.
A view should be something you can delete without changing how the game plays.

## 5.2 `Time` and `Random` from Unity in the domain

**Situation.** The AI should aim with a bit of randomness, and a buff should last 5 seconds.

**Don't.**

```csharp
var spread = UnityEngine.Random.Range(-0.1f, 0.1f);
_endsAt = UnityEngine.Time.time + 5f;
```

**What goes wrong.**
- The domain no longer compiles without Unity, so `dotnet test` and the architecture boundary are gone.
- `Time.time` keeps running during pause, so buffs expire while the game is paused.
- Unity's global random is shared with particles, UI and everything else. A replay of the same match produces
  different shots, because some particle effect consumed a random number first.

**Do.** Count time with the `deltaTime` the simulation gives you, and take randomness from the match's
`RandomStreams`: one named stream per feature.

```csharp
public sealed class Buff : IModel
{
    public float TimeLeft { get; private set; }
    internal bool Advance(float deltaTime) => (TimeLeft -= deltaTime) <= 0;
}

internal sealed class BrainSystem : EntitySystem<Brain, Position>
{
    private readonly GameRandom _random;   // random["ai"], from the match's RandomStreams
    ...
}

// composition root
Random = new RandomStreams(definition.Seed);
new BrainSystem(Context, ..., Random["ai"]);
new LootDropReaction(Context, ..., Random["loot"]);
```

**Why.** The simulation's time stops when the game is paused and replays exactly. A seeded generator owned by the
match gives the same numbers every time the match is replayed. Separate streams keep features apart: one more roll
in loot doesn't change what the AI does. `RandomStreams.Capture()` goes into the save, so a continued game rolls the
same dice as the one that was saved; `System.Random` can't do that because its state can't be saved.

## 5.3 Queries that return components

**Situation.** The HUD needs the player's health and ammo.

**Don't.**

```csharp
public Health HealthOf(EntityId player) => _world.Get<Health>(player);
```

**What goes wrong.** The presentation now holds the live component. Someone calls an `internal` method through
reflection "to fix a UI bug", or simply keeps the reference after the player died and shows stale numbers. Worse,
the HUD now depends on the internal shape of `Health`, so a refactor in Combat breaks the UI.

**Do.** Return a small snapshot with exactly what the reader needs.

```csharp
public readonly struct CombatStatus
{
    public float Health { get; }
    public float MaxHealth { get; }
    public int Ammo { get; }
    public bool IsReloading { get; }
}

public CombatStatus StatusOf(EntityId player) { ... }
```

**Why.** A snapshot is a copy. Nobody can change the game through it, it never goes stale in a confusing way, and
the feature can reorganize its components freely as long as the snapshot stays the same.

## 5.4 Events or reading every frame?

**Situation.** The UI must show health, ammo, a "reloading" bar and a damage popup.

Both styles are allowed. Pick per value:

| Read Queries every frame when... | Listen to events when... |
|---|---|
| the value is shown continuously (a health bar, a reload progress bar) | something happens once (a popup, a sound, a flash) |
| it changes smoothly over time | you need the "how much" of a single change (`-25`) |
| missing one change is harmless, the next frame fixes it | missing it means the player never sees it |

**Don't.** Rebuild the whole inventory UI every frame "to be safe", or animate a bar only from events and wonder why
it freezes when an event is missed after a reload of the scene.

**Why.** Reading a snapshot each frame is simple and self-correcting. Events are precise about moments. Most
screens use both: a bar reads `StatusOf` in `LateTick`, and a popup listens to `DamageApplied`.


## 5.5 Presentation subscribed to the domain bus

**Situation.** The inventory screen opens a "pick up?" prompt when an item lands next to the player, and the
answer goes out as a command.

**Don't.**

```csharp
_subscription = match.Context.Events.Subscribe<ItemPlacedInWorld>(e => _commands.Send(new PickUpCommand(player, e.Item)));
```

**What goes wrong.** A bus subscriber runs inside the action that published the event, so the command counts as part
of the simulation. It is not recorded, and a replay, which has no presentation, never sends it: the replay diverges,
and the bug report it came with is useless. It also changes the world in the middle of an action that the domain
thought was its own.

**Do.** Presentation listens through an `EventFeed` and delivers it after the match has ticked.

```csharp
_events = new EventFeed(match.Context);
_subscription = _events.Subscribe<ItemPlacedInWorld>(OnItemPlaced);

protected override void LateTick(float deltaTime) => _events.Deliver();
```

**Why.** Feed subscribers run outside the simulation: a command they send is ordinary input, recorded and replayed.
The bus stays for the domain (reactions, game flow), whose subscribers exist in a replay too.

---

# Part 6. Game flow, screens and windows

A game is more than a world that ticks: it has a menu, loading, the match itself, a results screen, pause and
windows on top. Mosaic splits this into two state machines:

- **The domain state machine** (`FlowState`) decides what the game is doing: menu, loading, gameplay, results.
  It lives in the domain and works without Unity.
- **The screen state machine** (`ScreenStateMachine`) shows one `UiScreen` for the current domain state. It follows
  the domain: a screen may lag behind (to finish an animation), but never runs ahead.

Windows and popups (`UiWindow`, `UiPopup`) open on top of the current screen and close when the screen changes.

## 6.1 Changing state from inside the simulation

**Situation.** When the player dies, the game should go to the results screen.

**Don't.**

```csharp
// GameplayState.cs
protected override void OnEnter()
{
    _defeated = Match.Context.Events.Subscribe<PlayerDefeated>(_ => Go(new ResultState(Context, Match)));
}
```

**What goes wrong.** The subscriber runs in the middle of a simulation action. `Go` exits the gameplay state right
there: it disposes subscriptions and stops the match while other subscribers of the same event are still waiting
to run, and the rest of the tick continues on a match that is already "over". Death animations, loot drops and the
final save may or may not happen depending on subscriber order.

**Do.** Remember the fact in the subscriber, change state after the tick.

```csharp
private MatchResult? _result;

protected override void OnEnter() =>
    _defeated = Match.Context.Events.Subscribe<PlayerDefeated>(e => _result = MatchResult.Defeat(e.Wave));

protected override void Tick(float deltaTime)
{
    if (!IsPaused)
        Match.Tick(deltaTime);

    if (_result is { } result)               // the tick is over, the world is consistent
        Go(new ResultState(Context, Match, result));
}
```

**Why.** A state change is a big event: things get created and destroyed. It should happen between actions, when
the simulation is not halfway through anything.

## 6.2 The screen decides where the game goes

**Situation.** The menu has a "New game" button.

**Don't.**

```csharp
// MenuScreen.cs (presentation)
_view.NewGame.onClick.AddListener(() =>
{
    var match = new Match(_arena.ToDefinition());
    _states.Change(new GameplayState(_context, match));   // the UI builds the game and switches state
});
```

**What goes wrong.**
- The rules of the game flow ("new game wipes the saved run", "continue needs a save") end up in button handlers.
  A gamepad shortcut or a cheat that starts a game has to copy them, and the copies drift apart.
- The flow cannot be tested without a scene: the logic sits in a screen.
- `Change` from outside skips the "ignore requests to an inactive state" protection (see 6.3).

**Do.** The domain state offers the actions, the screen calls them.

```csharp
// MenuState.cs (domain)
public void NewGame()
{
    Context.Profile.SetRun(null);
    Go(new LoadingState(Context, continueFromSave: false));
}

public bool CanContinue => Context.Profile.CurrentRun != null;

// MenuScreen.cs (presentation)
_view.NewGame.onClick.AddListener(_state.NewGame);
_view.Continue.interactable = _state.CanContinue;
```

**Why.** Screens show and ask, the domain decides. Then the whole game flow is covered by plain tests:
"menu → new game → loading → gameplay → defeat → results → menu" runs in milliseconds without Unity.

## 6.3 Clicks that arrive after the state has changed

**Situation.** The player double-clicks "Back to menu" on the results screen while the screen is fading out.

**What happens.** The first click switched the domain to the menu. The results screen is still visible for a
moment (it is playing its exit animation) and receives the second click.

**Don't.** Guard every button with your own flags (`if (_alreadyClicked) return;`), or call `StateMachine.Change`
from presentation code.

**Do.** Call methods of the state the screen was created for and let `Go` do its job. `Go` does nothing when the
state is no longer active, so the second click is simply ignored.

```csharp
// ResultScreen.cs
_view.BackToMenu.onClick.AddListener(_state.BackToMenu);   // safe to call twice
```

**Why.** The screen may lag behind the domain on purpose (animations). Because of that, every request from a
screen must be safe when its state is already gone, and `FlowState` makes it safe for you.

## 6.4 The screen starts the match

**Situation.** The gameplay screen needs a running match to show.

**Don't.**

```csharp
// GameplayScreen.Enter
_state.Match.Start();       // the view starts the game
```

**What goes wrong.** The game now starts only when a screen is shown. A test of the game flow has no screens, so
the match never starts there. If the screen is slow to appear (a long fade), the first seconds of the match are
lost or happen twice depending on timing.

**Do.** The domain state starts the match when it is entered. The screen attaches to whatever the world already
contains and listens from there.

```csharp
// GameplayState (domain)
protected override void OnEnter() => Match.Start(_start);

// GameplayScreen (presentation)
protected override void Enter()
{
    _views = new ViewSpawner(...);   // follows EntitySpawned / EntityDestroyed from now on
    // ...create presenters that listen to _views.Views.Created...
    _views.CatchUp();                // then creates views for entities that already exist
}
```

**Why.** The domain must work with no screen at all. Presentation is always allowed to arrive late and catch up.

## 6.5 Pause as a `bool` or `Time.timeScale`

**Situation.** The pause menu and the inventory both should pause the game.

**Don't.**

```csharp
// PauseWindow
protected override void Opened() => Time.timeScale = 0;
protected override void Closed() => Time.timeScale = 1;

// InventoryWindow
protected override void Opened() => _gameplay.IsPaused = true;
protected override void Closed() => _gameplay.IsPaused = false;
```

**What goes wrong.**
- Open the inventory, then the pause menu, then close the pause menu: the game unpauses while the inventory is
  still open. Every "pause source" overwrites the others.
- `Time.timeScale = 0` also freezes UI animations, tweens and particles in menus, and it does not stop the
  simulation if the simulation ticks with its own time.
- On a network game nothing can actually pause, but the code assumes it can.

**Do.** Windows declare that they need a pause; the domain state counts the requests.

```csharp
// a window that pauses the game while it is open
public override bool HoldsPause => true;

// the gameplay screen passes the request to the domain
protected override void PauseRequested(bool requested)
{
    _pauseHold?.Dispose();
    _pauseHold = requested ? _state.HoldPause() : null;
}

// GameplayState
public GameClock Clock { get; } = new GameClock();
public bool CanPause => true;                 // false in a network match
public IDisposable HoldPause() => Clock.HoldPause();
protected override void Tick(float deltaTime) => Match.Tick(Clock.Advance(deltaTime));
```

**Why.** A pause is held for as long as anyone needs it, and released only when the last holder lets go. Holds are
`IDisposable`, so a window that closes for any reason (Escape, screen change, an exception) releases its hold.
`GameClock` does the counting, and slow motion and hit stops go through it the same way (`SlowDown`), so they stop
the simulation, not the menus.

## 6.6 Keeping the match in a static field

**Situation.** The results screen needs the score, and the menu wants to show "last match" statistics.

**Don't.**

```csharp
public static class CurrentGame
{
    public static Match Match;   // set when gameplay starts, read from anywhere
}
```

**What goes wrong.** After "back to menu" the old match is still referenced, so its whole world stays in memory.
A new match overwrites the field while a results screen is still reading it. A replay builds a second match and
both fight over the same field. And tests run in parallel or in sequence start sharing a match by accident.

**Do.** Pass the match from state to state through constructors. Keep only what outlives the match (the profile,
statistics) in the shared context.

```csharp
Go(new ResultState(Context, Match, result));          // the results state receives the match explicitly

// ResultState
public ResultState(GameFlowContext context, Match match, MatchResult result) : base(context) { ... }
```

**Why.** Each state owns what it needs, and the match disappears as soon as the last state that uses it is gone.

## 6.7 Saving from the UI

**Situation.** Progress should be saved at the start of each wave and cleared when the player loses.

**Don't.** Save from a presenter that listens to `WaveStarted`, or from a "Save" button handler that collects data
from views.

**What goes wrong.** Saving stops working in any build or test without that screen. Two screens both save and
overwrite each other. The saved data comes from views (what was displayed), not from the game state, so it can be
off by one frame or rounded.

**Do.** The domain state decides when to save, and builds the save data through Queries.

```csharp
// GameplayState
private void OnWaveStarted(WaveStarted e)
{
    if (Match.Capture(e.Wave) is { } run)    // built from Queries, not from components or views
    {
        Context.Profile.SetRun(run);
        Context.SaveProfile();
    }
}
```

**Why.** When to save is a game rule ("checkpoints at the start of a wave", "a defeat wipes the run"). Game rules
live in the domain, where they are tested.

## 6.8 Windows that open anywhere

**Situation.** The inventory opens with Tab.

**Don't.** Bind the key globally and open the window from anywhere.

**What goes wrong.** Tab in the main menu opens an inventory of a player who does not exist. Tab on the results
screen opens it on top of the "Defeat" text. Each screen then gets its own `if` to block it, and a new screen
forgets to.

**Do.** The screen owns its windows and decides what may open on top of it.

```csharp
// GameplayScreen creates the inventory window and handles Tab itself.

// MenuScreen
protected override bool Allows(UiWindow window) => window is SettingsWindow;
```

**Why.** What makes sense on top of a screen is a property of the screen. Changing the screen closes all windows
and popups, so nothing from the old screen can leak into the new one.

## 6.9 Enter and exit that do not match

**Situation.** A state subscribes to events and holds resources.

**Don't.** Subscribe in the constructor, or in `OnEnter` without unsubscribing in `OnExit`.

**What goes wrong.** States are created before they are entered (`Go(new LoadingState(...))` constructs it first).
A subscription in the constructor fires for a state that is not active yet. A subscription that is never released
keeps reacting after the state is gone: a finished match still saves the profile on its events.

**Do.** Everything acquired in `OnEnter` is released in `OnExit`, in reverse order. Keep constructors to storing
parameters.

```csharp
protected override void OnEnter()
{
    _defeated = Match.Context.Events.Subscribe<PlayerDefeated>(OnDefeated);
    _waveStarted = Match.Context.Events.Subscribe<WaveStarted>(OnWaveStarted);
    Match.Start(_start);
}

protected override void OnExit()
{
    _waveStarted.Dispose();
    _defeated.Dispose();
}
```

The same applies to screens: what `Enter` creates (presenters, views, subscriptions), `Exit` disposes.

**Why.** A state is active only between `OnEnter` and `OnExit`. Anything it does outside that window is a bug,
and matching pairs make that easy to check in a review.

## 6.10 Garbage: what to worry about and what not

**Situation.** You profile the game and see allocations every time the state changes: a new state object, a new
screen, a new prefab instance. Should you pool all of it?

**Usually not.** What matters is **how often** memory is allocated, not that it is allocated at all.

| Happens | Examples | Garbage is |
|---|---|---|
| every frame, many times | commands from input and AI, events, queries, system ticks | **a problem**: the GC runs in the middle of a fight and the game stutters |
| many times per match | opening the inventory, a popup per pickup, a damage number per hit | **worth fixing** when it shows in the profiler |
| a few times per session | menu → loading → gameplay → results | **fine**: a short pause during a screen change is not noticeable |

Mosaic follows this split. The hot path (commands, events, `Query`, systems) does not allocate, and tests check it.
State changes create new objects on purpose: each state starts clean, with no fields left over from the last time.

**Don't.** Pool domain states, reuse a `Match` between games by "resetting" it, or cache screens across state
changes to save a few kilobytes.

**What goes wrong.** Every reused object needs a reset method, and every new field needs to be added to it. Sooner
or later one is forgotten, and the second match starts with the wave counter or the pause holds of the first one.
These bugs appear only on the second playthrough, so they are found late.

**Do.** Keep transitions simple and fix what happens often. For a window the player opens many times, let it keep
its view between openings:

```csharp
public sealed class InventoryWindow : UiWindow
{
    public override bool KeepsViewWhenClosed => true;   // hide on close, show the same view next time

    protected override void Opened() => _presenter.AttachPanel(Show(_prefab));
    protected override void Closed() => _presenter.DetachPanel();
}
```

`Show` returns the same instance on every opening, so the presenter must be ready to get a view it has already
set up: don't create its slots or subscribe to its buttons twice. The kept view is destroyed when the screen changes.

**Why.** Pooling has a cost too: reset code, stale state and harder reviews. Pay it where the profiler shows a
problem, and keep the rest straightforward.

---

# Part 7. Testing and debugging habits

## 7.1 Test through the same door as the player

**Don't.** Test by calling private helpers, or by setting component fields directly and checking other fields.
These tests pass while the real game is broken, because the real game never goes through those paths.

**Do.** Build the world without Unity, act through a command (or `Simulation.Run` for setup), check events or
Queries.

```csharp
[Test]
public void Reload_is_rejected_when_the_magazine_is_full()
{
    var match = new Match(TestArena(), throwOnSubscriberException: true);
    var player = SpawnPlayerWithFullGun(match);

    var result = match.Context.Commands.Send(new ReloadCommand(player));

    Assert.That(result.Reason, Is.EqualTo(ReloadError.MagazineFull));
}
```

**Why.** A test that uses commands and events checks what the player experiences. It also survives refactoring:
you can rewrite Operations completely, and as long as the behaviour is the same, the test stays green.

## 7.2 Always use strict mode in tests

Create test worlds with `throwOnSubscriberException: true`. In strict mode a crashing subscriber and a stale read
(see 3.4) throw immediately instead of writing a warning nobody reads. A test that would only log a warning in the
game fails loudly instead, at the exact line.

## 7.3 Turn a bug report into a test

When a tester sends a match recording:

1. Replay it in a test (`ReplayFormat.Read(text).Play(...)`). If the bug reproduces, you now have a test that fails.
2. Fix the bug. The test turns green.
3. Keep the test. This exact bug can never come back unnoticed.

If the replay reports a divergence instead, the bug depends on something outside commands: input that skips
commands (2.1), Unity time or random in the domain (5.2), or logic in a MonoBehaviour (5.1). That is a bug too.

## 7.4 Debug with the trace before the debugger

When "something happened and I don't know why", open **Window > Mosaic > Simulation**, press **Record**, reproduce,
press **Pause** and filter by the entity id or event name. The trace shows which command or system started it and
which reaction made the next change. Set breakpoints only after you know which class to look at.

## 7.5 Lock the roles with an architecture test

Most role rules rely on `internal` and reviews. Add one test class that checks the compiled game with
`ArchitectureRules` from `Mosaic.Testing` (reference the `Mosaic.Testing` assembly from your test assembly):

```csharp
public sealed class ArchitectureTests
{
    private static readonly ArchitectureRules Rules = new ArchitectureRules(typeof(Match).Assembly, typeof(World).Assembly)
    {
        Exclude = type => type.Namespace == "MyGame.Cheats",   // code that bypasses roles on purpose
    };

    [Test]
    public void Roles_hold() => Assert.That(Rules.CheckAll(), Is.Empty);

    [Test]
    public void Scanner_sees_operations() => Assert.That(Rules.ScannerSeesOperations(), Is.Empty);
}
```

A violation is either a bug or a sign the rule should change, together with this guide. One test per rule (the
methods behind `CheckAll`) gives clearer failures in a large game.

---

# Part 8. Naming

Good names make the role of a class obvious, so reviews and searches are faster.

| What | Pattern | Examples |
|---|---|---|
| Component | a noun for the aspect | `Health`, `Weapon`, `Backpack`, `Stunned` |
| Definition | component name + `Definition` | `HealthDefinition`, `WeaponDefinition` |
| Command | imperative verb + `Command` | `AttackCommand`, `UseItemCommand`, `ReloadCommand` |
| Rejection reasons | action + `Error` | `AttackError.NoAmmo`, `UseItemError.SlotEmpty` |
| Event | past-tense fact | `WeaponFired`, `EntityDied`, `ItemPickedUp` |
| Operations | feature or component + `Operations` | `HealthOperations`, `InventoryOperations` |
| Evaluator | feature + `Evaluator` | `CombatEvaluator`, `ArmorEvaluator` |
| Queries | feature + `Queries` | `CombatQueries`, `InventoryQueries` |
| Snapshot | what it describes + `Status` / `Snapshot` | `CombatStatus`, `InventorySnapshot` |
| System | what it ticks + `System` | `MovementSystem`, `ProjectileSystem` |
| Reaction | what it does + `Reaction` | `DropLootReaction`, `AddScoreReaction` |

**Fields that hold dependencies** are named by role when the class uses one of its own feature, and by feature
and role otherwise:

| The class uses | Field name | Examples |
|---|---|---|
| the Operations, Evaluator or Queries of its own feature, one of each | the role | `_operations`, `_evaluator`, `_queries` |
| those of another feature, or several of the same role | feature + role | `_healthOperations`, `_combatEvaluator`, `_inventoryQueries` |

Avoid fields like `_health`, `_coins` or `_combat` for these: they read like data ("the health", "a list of coins")
rather than like the object that changes or calculates it.

Evaluator methods read like questions or calculations (`IsAlive`, `FindNearestHostile`, `TotalDefence`).
Operations methods read like actions (`ApplyDamage`, `Heal`, `StartReload`, `Despawn`).

---

# Part 9. Code review checklist

Copy this into your pull request template.

- [ ] Components are changed only in Operations of the feature that owns them.
- [ ] Every change that others may care about publishes an event, from Operations.
- [ ] New player, AI or tool actions are commands; rejections use a feature enum.
- [ ] Commands and events are `readonly struct`s with ids and values, no references.
- [ ] Nothing outside one method keeps `Entity` or components; long-lived data keeps `EntityId`.
- [ ] Definitions and config assets are not modified at runtime.
- [ ] No `UnityEngine` types, `Time` or `UnityEngine.Random` in the domain.
- [ ] No new static state, singletons or service locators.
- [ ] Optional consequences of another feature are Reactions, not direct calls from the core feature.
- [ ] New systems are added to the single tick-order list, with a reason for their position.
- [ ] Every new subscription is disposed by its owner.
- [ ] Queries return snapshots, not components.
- [ ] Domain states change only between actions (flag in a subscriber, `Go` in `Tick`).
- [ ] Screens call methods of their domain state; they never build matches or call `StateMachine.Change`.
- [ ] Pause goes through holds (`HoldsPause`, `HoldPause`), not `bool` flags or `Time.timeScale`.
- [ ] What `OnEnter` / `Enter` acquires, `OnExit` / `Exit` releases.
- [ ] Tests act through commands, run in strict mode and check events or Queries.

---

# Part 10. Symptoms and likely causes

When you see the symptom on the left, check the causes on the right first.

| Symptom | Likely cause | See |
|---|---|---|
| UI or sound didn't react to something that clearly happened | the change was made outside Operations, so no event | 1.1 |
| UI reacted to something that didn't happen | the event is published by a handler or presenter, not by the code that made the change | 1.3 |
| A dead or invalid entity can do something | input or a tool calls Operations directly, skipping the handler | 2.1 |
| Replay diverges or a bug cannot be reproduced | input outside commands, Unity time or random in the domain, logic in a MonoBehaviour | 2.1, 5.1, 5.2 |
| `Has<T>` / `Get<T>` returns the old value right after a change | buffered change read back in the same action (stale-read warning in the console) | 3.4 |
| `EntityNotAliveException` | an entity is used after it was destroyed, often by a second call in the same frame | 3.6 |
| All enemies of a type changed at once | a definition was modified at runtime | 3.5 |
| Something targets or follows the wrong entity | an `Entity` or component kept in a field instead of an `EntityId` | 3.1 |
| A bug appears only when a debug window or HUD is open | an Evaluator or Query changes state | 1.4 |
| Tests pass alone but fail together | static state shared between tests | 4.2 |
| `MissingReferenceException` after leaving a match | a subscription was never disposed | 4.4 |
| Hits or interactions "lag one frame" | systems in the wrong tick order | 4.3 |
| A new system never runs | it was created but not added to the tick list | 4.5 |
| Things happen during pause | logic in `Update` or based on `Time.time` | 5.1, 5.2 |
| GC spikes during fights | class events or commands, or allocations in a system's tick | 2.2 |
| A hitch every time a window opens | the window instantiates its prefab on every open | 6.10 |
| A feature cannot be tested without building half the game | direct calls where a Reaction belongs, or static dependencies | 4.1, 4.2 |
| Loot, saves or animations sometimes missing at the end of a match | state changed from inside a subscriber | 6.1 |
| The game flow cannot be tested without a scene | the screen decides where the game goes | 6.2 |
| A double click does something twice after leaving a screen | the screen bypasses `FlowState` | 6.3 |
| Game unpauses while a window is still open, or menu animations freeze | pause as a `bool` or `Time.timeScale` | 6.5 |
| Memory grows after each match | the match kept in a static field or a subscription not disposed | 6.6, 6.9 |
| A window opens on a screen where it makes no sense | windows opened globally instead of by the screen | 6.8 |
