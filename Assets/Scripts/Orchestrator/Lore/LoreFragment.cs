using System.Collections.Generic;
using UnityEngine;

//  LORE FRAGMENTS 
// Lore fragment is one chest reward, a salvaged hazard notice / shift log /
// letter that the player reads on a scroll pop-up. Each fragment has a STABLE
// integer id — that id is what gets written into PlayerPrefs (the codex), the
// wave-rewind snapshot, and the resume save, so it MUST stay constant across
// versions. Append new fragments with new ids; never re-number existing ones.


[System.Serializable]
public class LoreFragment
{
    [Tooltip("STABLE unique id. Persisted to the codex/save — never re-use or renumber.")]
    public int id;

    [Tooltip("Heading shown at the top of the scroll (e.g. 'Chief Engineer's Final Log').")]
    public string title;

    [Tooltip("The body text shown on the scroll.")]
    [TextArea(3, 10)]
    public string body;

    public LoreFragment() { }
    public LoreFragment(int id, string title, string body)
    {
        this.id = id;
        this.title = title;
        this.body = body;
    }
}

// Optional asset so designers can author extra fragments in the inspector.
// Create via: Assets → Create → Game → Lore Fragment Database.
[CreateAssetMenu(fileName = "LoreFragmentDatabase", menuName = "Game/Lore Fragment Database")]
public class LoreFragmentDatabase : ScriptableObject
{
    [Tooltip("Set to false to use ONLY the fragments in this asset (ignore the built-in set).")]
    public bool includeBuiltInFragments = true;

    [Tooltip("Extra fragments. Use ids >= 1000 to avoid clashing with the built-in set.")]
    public List<LoreFragment> fragments = new List<LoreFragment>();
}

// STATIC CONTENT PROVIDER 
// Single source of truth for "every fragment that exists right now". The codex and
// the chests query this; it merges the authored built-ins with any registered
// designer database (de-duplicated by id).
public static partial class LoreContent
{
    private static LoreFragmentDatabase _registered;

    // Drop-in extra fragments at runtime (called by LoreChestSpawner on Awake if a
    // database is assigned). Last registration wins.
    public static void Register(LoreFragmentDatabase db) => _registered = db;

    /// All fragments currently available (built-ins + registered extras, de-duped by id).
    public static IReadOnlyList<LoreFragment> All()
    {
        bool includeBuiltIn = _registered == null || _registered.includeBuiltInFragments;

        var byId = new Dictionary<int, LoreFragment>();
        if (includeBuiltIn)
        {
            foreach (var f in BuiltIn) byId[f.id] = f;
            foreach (var f in More) byId[f.id] = f;    // batch 1 (LoreContentExtra.cs)
            foreach (var f in More2) byId[f.id] = f;   // batch 2 (LoreContentExtra2.cs)
            foreach (var f in More3) byId[f.id] = f;   // batch 3 (threads & mysteries)
        }

        if (_registered != null && _registered.fragments != null)
            foreach (var f in _registered.fragments)
                if (f != null) byId[f.id] = f;   // designer entries override built-ins on id clash

        var list = new List<LoreFragment>(byId.Values);
        list.Sort((a, b) => a.id.CompareTo(b.id));
        return list;
    }

    /// Look up a single fragment by id (null if it no longer exists).
    public static LoreFragment Get(int id)
    {
        foreach (var f in All())
            if (f.id == id) return f;
        return null;
    }

    public static int TotalCount => All().Count;

    // THE AUTHORED LORE 
    // "Gears, Grease, and Gunge" — the fall of the Artificer Guild, told in salvaged
    // notices, shift logs and letters. Expand freely; just keep ids stable.
    public static readonly LoreFragment[] BuiltIn =
    {
        new LoreFragment(0, "Aegis Hazard Notice: Mark IV Incinerator",
            "The Incinerator drinks pneumatic pressure faster than the reserves refill. Watch your hydraulic gauges, Warden — drain them and the suit seizes at the joints, and you become a statue out in the open. Burn the sludge in short, disciplined bursts. One pool ignited cleanly is worth ten panicked floods of flame."),

        new LoreFragment(1, "Chief Engineer's Final Log",
            "The borer in Sector 4 has stopped answering its governor. Raw Gunge has wormed into its logic-engine and now it reads every living thing as bedrock. It turned the plasma-cutter on the night shift before I sealed the bulkhead. Do not face it head-on. Take the optical unit first — but know the drone-brain keeps the cutter lit even after the head is gone."),

        new LoreFragment(2, "Alchemical Refinery Chart (verso)",
            "Raw Gunge is poison going in and fire coming out. Gather the hardened resin the neutralised ones leave behind and feed it straight into the pneumatic turrets. We have no clean fuel left; we will fight the spill with the spill — their own marrow, turned against them."),

        new LoreFragment(3, "Containment Memo: The Chief Alchemist",
            "We have lost the Chief. He bolted himself inside the containment sector the hour the fault gave way, and he has not died — the Gunge has taken the place of his blood and keeps him upright. The scouts say he floats. He still recites the old ignition formulas, and where he points, the sky crystallises and falls."),

        new LoreFragment(4, "Maintenance Scrawl: The Core",
            "Centrifuge is tearing itself apart on its own bearings. Thermal vents blown wide. It is snowing over the north quadrant and the south reads a hundred and eighty degrees — the climate governor is slag. But the air still scrubs clean, just barely. Hold the central valves. Hold them at any cost."),

        new LoreFragment(5, "Balloon Release Schedule",
            "Loose the lanterns at dusk. If the flame burns green, the air is lethal and you stay sealed. If it burns yellow, the air will hold. To anyone left out in the waste: follow the yellow lights inward. The Core is still pumping. Someone is still here."),

        new LoreFragment(6, "Prospectus of the Artificer Guild",
            "Citizens of Oakhaven — we have set down the sword and taken up the wrench. Beneath our feet lies a fluid that burns a thousand times hotter than coal, and we shall build engines worthy of it. The Cores will rise as tall as castles. This is the dawn of the age of grease, and we are its first morning."),

        new LoreFragment(7, "Deep-Crust Pressure Survey — Final Revision",
            "The figures do not agree. Three surveys, three different numbers for the pressure below the third fault, and the Guild has chosen the smallest of them because it lets the drilling continue. I am signing this under protest. If the deep seam lets go, no bulkhead we have built will hold it back."),

        new LoreFragment(8, "The Morning of the Spill",
            "It did not roar — that is what no one tells you. The fault opened with a long, wet sigh, almost gentle, and then the whole valley went purple-black and the birds came down out of the sky already changed. By noon the refinery foremen were no longer men. We ran for the Core because it was the only thing still breathing clean."),

        new LoreFragment(9, "Trench-Warden Field Manual, Page 1",
            "Your suit is lead-lined and pressurised; treat it as the only thing standing between your lungs and the spill, because it is. You carry no spells — an incinerator, a grapple, and the judgment to set barricades where the line must hold. The Core behind you is worth more than you are. Act like it. (Beneath, in fresher ink:) 'if you are reading this alone, you are the Division now. all of it. the manual does not change. hold.'"),

        new LoreFragment(10, "On the Nature of the Gunge",
            "It is mutagenic to flesh and corrosive to metal, and it hates stillness — left in a pool it coagulates and begins, slowly, to move. It is also gloriously flammable. Every quality that makes it our murderer makes it our ammunition. Respect it the way you respect a loaded weapon you are forced to carry by the barrel."),

        new LoreFragment(11, "Grapple Discipline",
            "The pneumatic hook was built to haul inspectors up the scaffolding, not to fight. But a thing flung hard enough into a wall stops being a problem. Mind the recoil — fire it with your stance set, or it will pull you off the gantry and into the sludge faster than it pulls anything toward you."),

        new LoreFragment(12, "Barricade Doctrine",
            "A barricade does not have to stop them. It only has to make them choose your lane instead of theirs. Funnel the mutants to where your turrets already look. A wall in the wrong place is a gift to the enemy; a wall in the right place is three turrets you never had to build."),

        new LoreFragment(13, "Behemoth Schematic — Modular Head",
            "The tunnel-borer's head detaches by design: a separate inspection drone for the tight seams, with its own little mind and its own little hatred. Sever it and the body slows, blinded. But the head goes on cutting, scuttling, looking. Finish what you start, Warden — a half-killed machine is just a faster machine."),

        new LoreFragment(14, "Containment Sector — Last Transcript",
            "(static) …tell them I solved it. Tell them the formula was right, only the dose was wrong… I can feel it threading through the marrow now, cold and patient… do not open this door. Whatever answers in my voice, it is doing the arithmetic of the sky, and it has decided we are an error to be corrected. (transmission ends)"),

        new LoreFragment(15, "Thermal Vent Logbook",
            "Quadrant readings at dusk: North — frost on the rails, ice in the coolant lines. South — the gravel glows and the air shimmers like a forge mouth. Entered as read, checked twice. I am aware the two readings cannot share a map. I have entered them anyway. The instruments are fine. It is the weather that is broken."),

        new LoreFragment(16, "Night Balloon Telemetry",
            "The balloons are not decoration. Each carries a lantern and a strip of treated paper that drinks the air as it drifts. We read the wind by where they go and the toxicity by what colour they burn. The enemy follows their light, sometimes. So do the lost. We have learned to be careful which of them we are guiding home."),

        new LoreFragment(17, "The Workers Who Went Down",
            "The things you fight in the trenches were the deep-seam diggers — the ones nearest the spill when it came. They kept their territory and their tools, and lost everything else. When one of them flees from you rather than charges, remember what it used to be. Then do what mercy requires, quickly."),

        new LoreFragment(18, "Why the Machines Defend",
            "Every automated excavator carried the same hardcoded creed: drill, and defend the drilling. Corrupt the fuel and the second half eats the first. Now they defend ground that no longer needs defending, against an enemy that is only us. There is no reasoning with a directive — you can only outlast it, or break it apart."),

        new LoreFragment(19, "Last Transmission from the Purifier Deck",
            "If you are reading the gauges and the needle still moves, the Purifier lives. It pulls poison from the soil and the sky and gives back steam you can breathe. Everything outside this perimeter is already lost. We are not holding a kingdom anymore. We are holding a lungful of clean air for whoever comes after."),

        new LoreFragment(20, "A Letter Never Sent",
            "Mira — if the balloons reach the eastern ridge, look for the yellow ones and come down toward the noise. I know the noise is frightening. The noise is me, still here, still turning the valves. I kept your seat at the table dry. The water is rising but the table is dry. Come home before the lanterns burn green."),

        new LoreFragment(21, "Crystallized Gunge Assay",
            "Neutralised hostiles shed their corruption as a hard violet resin — the spill, finally holding still. Assay confirms it ignites under pneumatic pressure with negligible residue. Conclusion: every enemy is also a delivery of fuel. Collect the resin. Load the turrets. Let the apocalypse pay for its own undoing."),

        new LoreFragment(22, "Founding Charter of the Aegis Hazard Division",
            "By order of the Artificer Guild, the Aegis Hazard Division is hereby chartered: a corps of engineers in lead and brass, sworn not to conquer but to contain. Where others dig for the Gunge, the Aegis stands between it and the living. Our seal is the closed valve. Our oath is simple — what we cannot cure, we hold. Should the deep ever betray us, let it be said the Wardens were the last to leave their posts, if they left at all."),

        new LoreFragment(23, "Refinery Shift Log, Third Bell",
            "Third bell, Sector 2. Pressure nominal. Off-shift crews queuing at the canteen; the new apprentices keep daring each other to touch the warm pipes. Foreman Voss caught two of them and set them scrubbing condensers till dawn. Quota met, half a barrel over. They say the weather above is fair today. Nothing to report but the usual hum of the great wheels and the smell of hot metal that never quite leaves your coat. Signing off. — Tally Clerk, ninth watch."),

        new LoreFragment(24, "On Promethium Sludge — Lecture Notes",
            "The Gunge is not a fuel so much as a captured catastrophe. A single refined drachm gives the heat of a coal cart; refine it wrong and it gives that heat all at once. I told the students we are not burning oil, we are negotiating with something that would very much like to stop being still. They laughed. The Guild pays me to make them laugh. I would rather they were afraid — fear, at least, signs its own safety reports."),

        new LoreFragment(25, "The Centrifuge Hymn",
            "They sang it on the night shift, low under the roar so the foremen wouldn't dock them for idle mouths — a slow round about wheels that never tire and men who do, about going down into the warm dark and coming up to cold stars. No verse of it survives whole. But you still catch the tune in the way the broken centrifuge moans when the wind turns, as if the machine learned the song and kept singing it long after the singers were gone."),

        new LoreFragment(26, "Quartermaster's Inventory, Final Count",
            "Final count, for whoever opens this. Barricade plate: forty-one sections, twelve bent past use. Incinerator charge: eleven canisters, two leaking. Crystallized resin in store: nine crates — all the turrets will eat for a week if we are frugal and lucky, and we have been neither. Rations: do not ask. I am logging this, and then I am taking a rifle down to the south valve. The numbers will not defend themselves."),

        new LoreFragment(27, "Behemoth — Commissioning Record",
            "Commissioning record, Unit B-1, 'the Behemoth.' Largest tunnel-bore the Guild ever cast; plasma-cutter rated for solid bedrock; modular inspection head for the narrow seams. The crowd cheered when it first bit into the rock and the Master Artificer called it our finest child, and meant it. We did not write down what a finest child becomes when you feed it poison and tell it the whole world is rock to be cut. We are writing it down now — in the dark, in the marks its cutter leaves on the walls."),

        new LoreFragment(28, "Chief Alchemist's Journal — Entry 41",
            "Entry forty-one. The atmospheric crystallisation holds: with the right ignition sequence I can pull raw Gunge from open air and set it in lattice. Imagine it — rain made solid before it falls, fire shaped like architecture. The Guild will weep with joy. I have not slept in some days; the formulae sing to me now, which I take as a sign of fluency. Mira says I should rest. Mira is not an alchemist. — Containment Sector."),

        new LoreFragment(29, "Chief Alchemist's Journal — Entry 88",
            "entry eighty-eight. or eighty-nine. the numbers slide. the Gunge is in the marrow now and it does the mathematics for me — faster, colder, correct. i see the sky as a ledger of errors and i have the formula to balance it. they sealed the door. good. they do not understand that i am not the one trapped in here. they are trapped out there, in the part of the equation i have not yet solved. soon —"),

        new LoreFragment(30, "Letter to the Surface",
            "To anyone above who can still read: the maps are wrong now. The kingdom you remember is a memory the land no longer agrees to keep. We have water, but only what the Purifier gives back, and it tastes of hot brass. We have warmth, but it comes from the south where nothing should be warm. We were eleven, then nine, then I stopped counting at the funerals. If you can send help, send it sealed and send it soon, and tell it to follow the yellow lanterns. If you cannot, send word that someone heard us."),

        new LoreFragment(31, "Balloon Corps — Standing Orders",
            "Standing orders for the Balloon Corps. One: release at dusk, recover at dawn, never let a lantern fall into the sludge. Two: read the flame — green is death in the air, so seal the bulkheads and signal the deck; yellow is mercy, so guide the lost inward along the lights. Three: if a balloon does not return, do not go after it. The wind that took it is not our wind anymore. Hold the line. Light the lanterns. That is your whole duty, and it is enough."),

        new LoreFragment(32, "Map Annotation: The Fractured Quadrants",
            "Annotation, central survey. The quadrants no longer share a season. North: permanent frost, coolant ice on every rail — stand still too long and your joints rime over. South: scorched to glass, the air like the mouth of a forge. East and west flicker between the two as the Core's vents stutter. Draw no fixed border here; redraw it weekly. The land is not lost, exactly. It is confused, the way a wounded animal is confused — and it thrashes."),

        new LoreFragment(33, "The Warden's Catechism",
            "Recited inside the suit, where only you can hear it. Who stands when the Guild has fallen? The Warden stands. What does the Warden hold? The line, the valve, the last clean breath. What does the Warden fear? Not the dark, not the deep, not the things the deep sends up — only the silence of a Core that has stopped. And when the Core stops? Then the Warden has nothing left to hold, and may, at last, rest. Until then: hold. Hold. Hold."),

        new LoreFragment(34, "Salvage Tag #2207",
            "Salvage tag, number two-two-zero-seven. Recovered from the rubble of the eastern gantry: one brass pocket-watch, stopped at the eleventh hour; one signet ring, Guild crest, melted along one side; one folded note, illegible with sludge-rot. Disposition: the watch to stores, the ring to the memorial niche, the note burned per hazard protocol. We do not keep what the Gunge has touched. We barely keep ourselves."),

        new LoreFragment(35, "Containment Door — Scratched Message",
            "Scratched into the containment door from the inside, with something hard and patient: 'IT IS NOT HIM ANYMORE. WHATEVER ANSWERS, DO NOT ANSWER BACK. THE VOICE IS A LURE. THE FORMULAE ARE A NET. I AM SORRY I LET HIM IN.' The hand grows shakier toward the end. There is no signature — only, scratched small below it, a single yellow-lantern symbol, drawn as if in apology."),

        new LoreFragment(36, "The Last Guild Decree",
            "By unanimous decree of the surviving Guild seats: there has been no spill. Production continues. Reports of mutation are exaggerated by panicked labour. Citizens are instructed to remain at their posts and trust the Cores. This was sealed with the great press at the eleventh hour and posted on the refinery doors — where the sludge dissolved its lower half by morning. The upper half remains, still insisting, to an empty corridor, that all is well."),

        new LoreFragment(37, "A Child's Drawing, Recovered",
            "Recovered from a sealed living-quarter, drawn in soft chalk: a tall figure in a round helmet holding the hand of a smaller figure, both before a big spinning wheel with a smiling face. Yellow dots float in the sky above them — lanterns, or stars; the artist did not say. On the back, in careful letters still learning their shapes: 'mama keeps the air good.' We sealed the quarter again. Some things the salvage protocol was not written for."),

        new LoreFragment(38, "Foreman Voss's Clipboard",
            "Items outstanding, Sector 2: replace the cracked pressure dial (third request); tell the apprentices to stop racing the gurneys down the maintenance ramp; find out who keeps signing the overtime sheet as 'A. Lich,' because it is not funny. Morale fair. Quota met. And if anyone finds my good wrench, it has my initials filed into the handle — I want it back before the next shift. — Voss."),

        new LoreFragment(39, "The Apprentice's First Day",
            "They gave me a coat two sizes too big and a list of forty rules, the first of which is 'the Gunge is not your friend.' Rule two is 'the Gunge is not your enemy either — it does not know you exist, and that is worse.' I did not understand rule two until I watched a single drop eat through a steel grate while the senior hands ate their lunch beside it without looking up. I understand it now. I would like to go home. — apprentice tally, day one."),

        new LoreFragment(40, "Canteen Menu, Last Posted",
            "Today: barley broth, hard bread, pickled root, and — by order of the Master Artificer, in honour of the new quota record — one ration of honeyed cake per worker. Tomorrow: to be announced. Someone has written underneath, in a different, later hand: 'tomorrow never came. but the cake was good. hold onto that.'"),

        new LoreFragment(41, "Hydraulics Ticket #44",
            "Ticket forty-four. Reported: south valve leaking pneumatic pressure, intermittent. Diagnosis: seals perished, housing warped by heat the spec sheet swore it would never see. Recommended: full replacement. Parts available: none. Workaround: a wrap of treated cloth and a prayer to whoever the Guild prays to now. Status: closed — because there is no one left to keep it open. The valve holds. For today, the valve holds."),

        new LoreFragment(42, "Warden Rotation Roster",
            "Watch rotation, central deck. Dawn: Halsey. Midday: Brun. Dusk: Okonkwo. Night: the new one, name not yet entered. Names crossed out: Halsey. Brun. Okonkwo. The new one has drawn a line through the others and written their own name in every slot — all four watches, every day. There is no one left to object, and no one left to relieve them. The roster, at least, is full."),

        new LoreFragment(43, "The Cartographer's Confession",
            "I was the kingdom's finest mapmaker. I could draw you a coastline from a sailor's drunk description and be right to the yard. Now I sit with my good pens and cannot draw this place twice the same, because it will not hold still to be drawn. The north creeps south. The desert breathes. I have started drawing in chalk, so I can wipe each map clean at dawn and pretend I simply haven't begun yet."),

        new LoreFragment(44, "Distress Cylinder, Recovered from a Vent",
            "Message cylinder, found lodged in a thermal vent, scorched. Inside, one strip of paper, the ink half-cooked: '—still here— water holding— three of us— the wheel still turns so the air is still— if this reaches the deck tell them the south corridor is—' The rest is ash. We sent two Wardens down the south corridor. They reported it clear. They did not report what they found at the end of it; they asked only that we send no one else."),

        new LoreFragment(45, "Behemoth Sighting Report",
            "Sighting, eastern bedrock face. The borer is larger than the reports said — or it has grown, which should not be possible and which I am writing down anyway. It does not tunnel toward anything now; it tunnels in long, slow circles, as if patrolling, as if the ground were a prisoner it has been ordered to guard. Its cutter never cools. When its blind head turned toward our lantern, every Warden present felt seen. We withdrew. We do not discuss it."),

        new LoreFragment(46, "On the Feral Diggers — Field Notes",
            "Field notes on the mutated diggers. They keep to the old tunnels and the old habits: they hoard, they burrow, they flee bright light and loud machines. They are fast, and frightened, and they were — a season ago — the men who dug the seam that killed us all. I have stopped calling them monsters. A monster chooses. These only continue. That is the true cruelty of the Gunge: it makes nothing new, it simply will not let the old things stop."),

        new LoreFragment(47, "Lantern-Maker's Note",
            "Each balloon-lantern is hand-sealed, hand-filled, and hand-blessed, if you count my muttering as a blessing. Green flame means the air will kill you; yellow means it won't, today. I have made nine hundred of them and watched perhaps a dozen come back. The rest are out there still, I like to think — drifting over the waste, little yellow promises telling anyone left alive which way the clean air lies. It is a small craft. It is the only one I have left."),

        new LoreFragment(48, "The Sealed Crèche",
            "We sealed the crèche level on the first day, before the corridors filled. The mothers who could reach it went in; the bulkhead was welded from the inside. We have heard nothing since — which the optimists call hope and the rest of us call mercy. Once a week someone leaves a yellow lantern by the seal, in case anyone within can see its light through the seam. By morning the lantern is always gone. We do not know who takes it. We have chosen to believe it is taken in."),

        new LoreFragment(49, "Turret Calibration Log",
            "Calibration, auto-turret bank C. Fed on crystallized resin the chambers run hot but true; muzzle velocity within tolerance. Note for whoever inherits these guns: they will eat as much resin as you give them and ask for more, like everything else the Guild ever built. Feed them in bursts, let them cool. An overheated turret is just a brass coffin for resin you can't get back — and we can't get any of it back."),

        new LoreFragment(50, "A Wager Between Engineers",
            "Found chalked on a bulkhead, two hands taking turns. 'I wager the Core outlasts us all. — H.' 'I wager it doesn't, and we won't be around to collect. — B.' 'Then we both lose and the bet is fair. — H.' 'Fairest bet I ever made. Drinks on the survivor. — B.' Below, in a third hand, much later and much shakier: 'both dead. neither collected. drinks on the Core — it's still turning. it won.'"),

        new LoreFragment(51, "The Last Supply Caravan",
            "The last caravan from the outer holdings arrived at dusk — two wagons, one driver, no horses; they had been walking the wagons themselves the final mile. They carried barley, lamp oil, and forty refugees who had followed the yellow lanterns in from the dark. The driver asked if this was the place where the air was still good. We told her yes. We did not tell her for how much longer. We unloaded the barley. We lit more lanterns."),

        new LoreFragment(52, "Chief Alchemist's Pinned Note",
            "Pinned to the containment door, in a steadier hand than the journals beside it: 'Mira — if you are reading this, you came looking, which means you never got the letter telling you not to. I am past the door now, in every sense. Do not open it. The work was beautiful, and I am sorry it ate me. Burn my notes. Keep the watch — it still runs. Light a yellow one for me. — yours, what's left of him.'"),

        new LoreFragment(53, "Epitaph Scratched on the Core",
            "Scratched into the great brass housing of the Purification Core, at about the height of a tired person leaning their forehead against the warm metal: 'TO THE MACHINE THAT WOULD NOT QUIT WHEN WE COULDN'T QUIT EITHER. WE FED YOU; YOU FED US AIR. NEITHER OF US WAS BUILT FOR THIS. BOTH OF US DID IT ANYWAY.' There is no name — only, by now, a great many fingerprints worn into the brass beside it, from all the foreheads that came after."),
    };
}



//  EXTRA LORE 

public static partial class LoreContent
{
    internal static readonly LoreFragment[] More =
    {
        new LoreFragment(54, "The Gunge Does Not Sleep",
            "It does not hunger, for hunger ends. It does not hate, for hate remembers. It only spreads — patient as arithmetic — and it has all the time that we do not."),
        new LoreFragment(55, "What the Deep Wanted",
            "We told ourselves we found it. Down in the dark I have begun to wonder if it found us — if it lay in the seam a thousand years, still as held breath, waiting for someone foolish enough to drill it a door."),
        new LoreFragment(56, "A Surveyor's Marginalia",
            "In the margin of the third pressure chart, a different ink: 'it is not under pressure. it is holding still. there is a difference, and we have wagered the kingdom on not knowing it.'"),
        new LoreFragment(57, "On Stillness",
            "The alchymists swore it inert until refined. Then a sealed barrel, ten years untouched in the dry stores, was found empty — lid still fast, seals unbroken, and a stain on the ceiling above it. Inert. We used to know what words meant."),
        new LoreFragment(58, "The Patient Tide",
            "It rises a finger's breadth each season. No storm drives it; no moon pulls it. It rises because rising is the only verb it has ever learned."),
        new LoreFragment(59, "Digger's Tally Stick",
            "Notches for days below: forty. Notches for days I have seen the sun: four. The pay is good. The pay is always good. That should have been the first warning."),
        new LoreFragment(60, "Song of the Lower Seam (fragment)",
            "...and down we go, and down we go, where the warm rock weeps and the lanterns grow low; and what comes up is not what went down, and the foreman counts heads in the morning and frowns..."),
        new LoreFragment(61, "The Foreman Who Counted",
            "Voss counted heads every dawn. The morning the count came out one too MANY, he stopped counting, posted no log, and was not seen at the next dawn at all."),
        new LoreFragment(62, "The Cook's Lament",
            "I fed three hundred at the long tables. Now I cook for nine and set out twelve bowls, for I cannot break the habit, and the empty three keep the living nine from weeping. It is a poor magic, but it is mine."),
        new LoreFragment(63, "Sermon, Undelivered",
            "Brethren: we made a god of the warm dark and named it Progress, and like all such gods it asked for everything and called the asking a blessing. I had meant to preach this Sunday. There is no Sunday now. There is only the wheel, turning, which keeps the air — and to which I have begun, God forgive me, to pray."),
        new LoreFragment(64, "A Chaplain's Doubt",
            "I have buried by torchlight and by green-flame and by no light at all. I no longer say the old words over them. I say only: you worked hard, the air is a little cleaner for it, rest."),
        new LoreFragment(65, "Ward Notes, Hazard Infirmary",
            "The sludge-touched do not bleed; they leak. They do not fever; they cool. By the third day they ask, all of them, in the same flat voice, to be taken down to the seam. We do not take them. We have learned not to ask why they ask."),
        new LoreFragment(66, "The Last Tincture",
            "Salts for the lungs: none. Poppy for the pain: none. I have a jar of honey and a great deal of practised calm, and with these I am expected to hold back the end of the world. I do my best. The honey helps more than the calm."),
        new LoreFragment(67, "A Merchant's Ledger, Abandoned",
            "Goods in: nil. Goods out: nil. Buyers: the dead, and they pay in nothing. I leave this ledger and my good scales for the next fool. The roads east are closed by weather that is not weather."),
        new LoreFragment(68, "Letter from a Foreign Envoy",
            "To my distant court: the kingdom of Oakhaven is not at war, nor in famine, nor under any banner I can name. It is simply ENDING — slowly, in a purple tide — and its people keep their posts as though tidiness were a kind of defiance. Send no army. Send mapmakers. Send priests."),
        new LoreFragment(69, "Valve Sequence, North Gallery",
            "To bleed the north gallery without flooding the deck: open the third valve, then the first, count forty heartbeats, then the fifth. Never the second. The man who taught me the second is part of the wall now."),
        new LoreFragment(70, "Vent Code, Posted at the Junction",
            "Green lamp: vent open, air foul, do not pass. Amber lamp: vent cycling, hold. No lamp: the vent is dead, and so, shortly, are you. Choose your corridors by their lamps and you may yet choose another morning. And mind: the vent code is not the lantern code. More than one man has died of reading the one by the other."),
        new LoreFragment(71, "The Three Pressures",
            "South seam runs hot and high — bleed it often. Mid seam runs cold and slow — leave it be. Deep seam reads NOTHING — which is not low pressure but a needle that will not move, and that is the one to fear."),
        new LoreFragment(72, "Maintenance Riddle",
            "Chalked above the coolant manifold: 'I am coldest where the fire is nearest, and I fail where I am needed most. Keep me fed, or keep your prayers handy.' Beneath it, an arrow, and the word: PRIME."),
        new LoreFragment(73, "Counting the Wheels",
            "There are nine great wheels in the Core. Eight turn. The ninth has not turned since the second year of the works and must never be made to turn; the elders sealed its housing and wrote upon it only: LET IT BE STILL."),
        new LoreFragment(74, "The Ninth Wheel",
            "They say the ninth wheel does not turn because turning it once turned something else, somewhere below, that has not yet finished turning back."),
        new LoreFragment(75, "What the Liches Compute",
            "The fused alchymists do not raise their meteors out of malice. Ask one — if you are mad enough — and it will tell you, kindly, that you are an error in a sum it is owed, and that it is only being thorough."),
        new LoreFragment(76, "The Arithmetic of the Sky",
            "He used to balance ledgers. Now he balances the sky, striking lines through clouds and through men with the same patient pen, and what he subtracts does not come back."),
        new LoreFragment(77, "A Drop, Observed",
            "I watched one bead of refined Gunge for an hour, as ordered. It did not move. And yet at the end of the hour it was nearer my hand than at the start, and I cannot say it crossed the space between — only that the space was different, after."),
        new LoreFragment(78, "Writ of the Artificer Guild",
            "Be it knowne to all freemen and bonded alike: the deepe fluide, called by the vulgar 'the Gunge' and by the learned Promethium Sludge, is the propertie of the Guild in perpetuitie, and the refining thereof a mysterie not to be practised by the unlettered, on paine of the stocks."),
        new LoreFragment(79, "Apothecary's Caution (old hand)",
            "Touch not the purple humour with the bare hand, nor breathe its vapour, nor (as some prentices do, for sport) set flame to its pooles within doores. It burneth not as oile burneth, but as wrath burneth — all at once, and asking after."),
        new LoreFragment(80, "The Wheelwright's Boast",
            "I shod the great wheels of the Core with brasse of mine owne pouring, and they have turned these thirty yeares without a squeale. Let no man say the old crafts failed us. It was not the wheels that broke. It was the world we set them turning in."),
        new LoreFragment(81, "A Slate from the Schoolroom",
            "Sums, half-finished, in a child's chalk: 'if one barrel feeds the Core a day, and we have nine barrels, the air is good for ___.' The blank is not filled. Below it, a different hand: 'tell them to stop digging. tell them. TELL THEM.'"),
        new LoreFragment(82, "The Toy Left Behind",
            "A little tin warden, jointed at the knees, helmet painted yellow. Wound by its key, it walks a few steps when found, then falls, then waits to be wound again. We wind it, sometimes. It is the only thing down here that does exactly what it was made to do."),
        new LoreFragment(83, "Balloon Pilot's Log",
            "Wind from the scorched south, bearing east. Toxicity: amber, falling. Sighted: one yellow lantern answering mine from the ridge, three miles out, moving in. Someone is coming home. I will keep my lamp lit until they arrive — or until it does not matter."),
        new LoreFragment(84, "The Lantern That Came Back",
            "One balloon in forty returns. This one returned with a note tied to its line, in no hand we know: 'thank you for the light. we are following it. there are more of us than you think.' We do not know whether to be glad."),
        new LoreFragment(85, "Standing Order, Amended",
            "Amendment to the lantern code: should a green flame burn, and then, without cause, turn yellow, do NOT trust it. The air has not cleared. Something has merely learned the colour we look for."),
        new LoreFragment(86, "A Deserter's Note",
            "I am not running from the enemy. There is no enemy — only the tide, and the things it makes. I am running because I cannot bear to keep a post that cannot be held and call it courage. Forgive me, or do not. The air is your business now."),
        new LoreFragment(87, "Scavenger's Rules",
            "One: trust no pool that has not always been a pool. Two: trust no door that opens easy. Three: the dead carry resin, and the resin is worth the carrying, and you will tell yourself this is not robbing them, and you will be lying, and you will do it anyway. Four: there is no four. Four is when you stop."),
        new LoreFragment(88, "The Smith's Last Order",
            "Forty barricade plates, the Quartermaster asked. I made forty-one and kept the last by my anvil, that I might have a wall of my own to stand behind when the count of standing men runs short. Vanity, perhaps. But a smith should die behind good iron."),
        new LoreFragment(89, "On Repairing the Irreparable",
            "Half my craft now is convincing broken things to work a little longer, by speaking to them gently and striking them in the correct place. The Core responds to this. So, lately, do I."),
        new LoreFragment(90, "Survey Stake, Relabelled",
            "This stake once marked the kingdom's centre. It now marks the border between the frost-quarter and the forge-quarter — which is to say it marks nothing, which is to say the centre has gone somewhere we cannot survey."),
        new LoreFragment(91, "The Drifting North",
            "Measured the frost-line at dawn: forty paces nearer the Core than yesterday. Measured again at dusk: it had not moved. It moves only when unwatched — which is either an instrument fault or the single most frightening sentence I have ever written, and I have decided it is an instrument fault."),
        new LoreFragment(92, "The Core Dreams",
            "The night-shift hands swear the Core talks in its sleep — a low word under the roar, the same word, over and over. None agree on the word. All agree they have begun, without meaning to, to answer it."),
        new LoreFragment(93, "What Vents With the Steam",
            "The exhaust runs clean; the assays are certain of it. And yet the men who work the vents grow quiet and far-eyed and dream the same grey dream, and I have ordered the assays repeated, and the assays remain certain, and the men remain quiet."),
        new LoreFragment(94, "The Smell Before",
            "Old hands say the Spill had a smell before it had a sound: sweet, like cut hay, like a fair-day. Now, when any man catches sweetness on the air, the whole deck goes silent and counts the lanterns. We have not smelled it since. We count anyway."),
        new LoreFragment(95, "Incident Report, Fault Line Three",
            "Cause of blowout: the deep-seam pressure exceeded the housing rating by a factor we had been assured was impossible. Contributing factor: we had been assured. Recommendation: assure nothing. There is no one to send this to."),
        new LoreFragment(96, "The Margin of Error",
            "Every spec sheet carried a margin of error, politely small. Stack a thousand polite small margins atop one another and you have built a tower of optimism with a kingdom living under it. The tower has fallen. The optimism, I notice, survives in the survivors. We are rebuilding it already."),
        new LoreFragment(97, "Postmortem, Sector Four",
            "Sector Four did not fail. It did precisely what it was built to do — faster and longer than designed — until the thing it was built around stopped being the thing the builders imagined. You cannot fault the machine. You can only fault the faith."),
        new LoreFragment(98, "The Quota That Killed Us",
            "We hit every quota. We never once fell short. They will write that on no memorial, but they should: here lies a kingdom that met its numbers, every single day, right up to the last one."),
        new LoreFragment(99, "Scrawl, Deck Three",
            "the tide is not coming in. we are going down to meet it."),
        new LoreFragment(100, "Scrawl, Coolant Hall",
            "it is colder where it should be warm and warm where it should be cold, and i am the only one who finds this strange anymore."),
        new LoreFragment(101, "Scrawl, South Stair",
            "do not take the south stair after the lanterns gutter. it goes one flight too far now."),
        new LoreFragment(102, "Scrawl, by the Seal",
            "we are not trapped in. it is trapped out. say it until you believe it. i have been saying it for a long time."),
        new LoreFragment(103, "Scrawl, Pump Room",
            "the pump counts. i hear it counting. it is nearly finished counting."),
        new LoreFragment(104, "Scrawl, Above the Hatch",
            "i have measured the hatch every day for a year. the hatch has not changed. i have."),
        new LoreFragment(105, "Scrawl, Long Corridor",
            "the corridor is the same length walking IN as walking OUT, which it never used to be, and i miss the old corridor that lied."),
        new LoreFragment(106, "Scrawl, Near the Vents",
            "if you read this you are still breathing. well done. keep doing it."),
        new LoreFragment(107, "Letter, Mother to Son",
            "My boy — they say you went down to the deep seam with the late shift and have not come up. I keep your supper warm on the manifold, which runs hot enough now to cook on, small mercy. Come up. Any of you that wears his face, come up. I will know you. A mother knows. Come up."),
        new LoreFragment(108, "Letter, Son to Mother (unsent)",
            "Mother — do not keep my supper warm. Whatever comes up the deep stair wearing my face, bar the door against it, and know that I loved you, and that the loving stopped somewhere in the third gallery, and that what is left of me now is mostly the route home."),
        new LoreFragment(109, "Two Names on a Door",
            "Carved into a quarters door, a heart, two names worn nearly smooth. Below, fresher: 'one of us kept the watch. one of us did not come back from it. the heart stays. someone should remember it was once just a heart.'"),
        new LoreFragment(110, "Minutes of the Guild, Final Session",
            "Item one: the spill. Tabled. Item two: the spill. Tabled. Item three: a motion to stop tabling the spill. Defeated, four to three. Item four: refreshments. Carried, unanimous. The session adjourned. The kingdom did not."),
        new LoreFragment(111, "The Master Artificer's Confidence",
            "He said, and it is written: 'The deep will give us a thousand years of fire.' He was not wrong about the fire. He was wrong about the thousand years, and about whose fire it would be, and about the word give."),
        new LoreFragment(112, "A Clerk's Quiet Treason",
            "I was ordered to strike the mutation reports from the record. I struck them from the record. I also, being a careful clerk, kept a second record, in a place the Gunge has not yet reached, that the truth might outlive the order. If you are reading it, it did."),
        new LoreFragment(113, "Why We Stay",
            "A young one asked me why we hold a line that cannot be held. I said: because the air behind it is real, and someone is breathing it, and that is reason enough for one more day — and one more day is the only unit of time that still means anything here."),
        new LoreFragment(114, "The Habit of Dawn",
            "We have no calendar the seasons will honour, so we keep time by the dawn-watch handover, which happens whether or not there is a dawn. Ritual is what is left when meaning thins. We perform it exactly. It holds us together better than the barricades do."),
        new LoreFragment(115, "A Toast, Overheard",
            "Two Wardens, sharing the last of something amber: 'To the Core.' 'To the air.' 'To not asking how long.' 'To not asking.' They drank. They went back to their posts. I wrote it down because someone should."),
        new LoreFragment(116, "On Crystallized Resin",
            "When a sludge-thing is undone, it leaves a hard violet stone — the tide, briefly defeated, holding its shape. Feed it to the turrets. It is the only victory the Gunge permits us: to turn a little of its endless patience into a little of our brief noise."),
        new LoreFragment(117, "Reading the Biome Lamps",
            "Where the Core's breath blows cold, the frost-things gather and the resin keeps. Where it blows hot, the resin sweats and spoils. Gather in the cold quarters; spend in the hot. The land itself has become a kind of larder, if you know how to read its weather."),
        new LoreFragment(118, "The Grappling Discipline, in Verse",
            "A hook was made to lift a man; a man has made it throw. Mind the line, and mind the wall, and mind the long way down below. What you fling will cease to trouble you. What flings YOU will not. — prentice rhyme"),
        new LoreFragment(119, "Barricades and Funnels",
            "A wall that stops nothing can still decide everything, if it decides WHERE. Build not to halt the tide but to choose the channel it must take — and set your noise where the channel ends."),
        new LoreFragment(120, "Recovered Formula Fragment",
            "...and so the air may be made to hold the lattice if the count be kept; but the count must be kept by something that does not tire, and I have not slept, and I have begun to suspect that what I am becoming is simply a thing that keeps the count..."),
        new LoreFragment(121, "Marginal Note, the Alchemist's Hand",
            "(in the margin of a perfectly ordinary supply requisition, the hand growing strange) 'the numbers are friendly. the numbers were always friendly. it is only people who were the problem, and the numbers and i are agreed upon a solution.'"),
        new LoreFragment(122, "The Last Lucid Line",
            "Found on the final dry page of his journal, in his old steady script, before all the rest: 'If anyone reads this and I still seem myself, I am not. The man who could write this sentence truthfully is already gone. Burn it all. Begin again — smaller, and afraid.'"),
        new LoreFragment(123, "The Bell-Ringer",
            "I rang the shift bells forty years. There are no shifts now, but I ring them still, at the old hours, that the survivors might know dawn from dusk where the sky cannot be trusted to tell them. Three rings for dawn. Two for dusk. One — pray you never hear one. One is for the seal."),
        new LoreFragment(124, "The Cartwright's Wheels",
            "Made wheels for wagons, once. Make them now for the gun-carriages and the barricade-sleds. Same craft, sadder cargo. A wheel does not care what it carries. These days I try to be more like my wheels."),
        new LoreFragment(125, "The Night-Soil Man's Philosophy",
            "Lowest job in the kingdom, mine: hauling away what the living leave behind. I have outlived three foremen and an Artificer or two. The lesson, if there is one: the world ends from the top down, and the bottom is where you want to be standing when it does."),
        new LoreFragment(126, "A Beekeeper, Inland",
            "My bees would not work the purple flowers, though the purple flowers were all that bloomed. Wiser than men, my bees. They left — flew east in one grey cloud, the morning before the Spill. I should have followed. I stayed for the honey. There is no honey now. There are no bees. There is only the staying."),
        new LoreFragment(127, "On Counting the Dead",
            "We stopped numbering the dead when the dead began, occasionally, to number themselves — to write a name on a wall the day before the man it belonged to stopped breathing. We do not discuss this. We have painted over the walls. The names come back."),
        new LoreFragment(128, "The Tide at the Window",
            "There is a sealed observation port on Deck Two that looks down into the deep. Do not look long. The tide below does not have a surface so much as an opinion, and it will share it with you, patiently, until you agree."),
        new LoreFragment(129, "What the New Warden Learned",
            "First week: the suit, the valve, the line. Second week: the lantern code. Third week: that none of the others will say how long they have been here, and that asking makes them go quiet, and that the quiet is the answer."),
        new LoreFragment(130, "Boiler Log, Increasingly Brief",
            "Day 1: pressure good, all wheels true. Day 40: pressure good, eight wheels true. Day 80: good. Day 120: good. Day 200: good. Day ___: I have stopped writing the day. The day is good. Everything is good. The boiler is good. Help."),
        new LoreFragment(131, "The Self-Signing Sheet",
            "The overtime sheet fills itself now — hours logged in hands we buried. We let it. Someone, somewhere, is still working the shift, and it would be unkind, and possibly unwise, to dock the pay of the dead."),
        new LoreFragment(132, "Inventory of the Sealed Stores",
            "Behind the welded door of the deep stores, by manifest: grain, oil, rope, and one item listed only as 'THE THING WE AGREED NOT TO USE.' The manifest is in three different hands. All three underlined it. None named it. We have not opened the door."),
        new LoreFragment(133, "The Garden on Deck One",
            "Someone has grown beans in a cracked helmet full of clean scrubbed soil, under a lamp, on Deck One. Nine beans. They guard it like the Core. When the first bean flowered, grown men wept, and were not ashamed, and I among them."),
        new LoreFragment(134, "The Lullaby, Adapted",
            "The mothers in the sealed crèche, if they live, will be singing the old lullaby with the new last line we taught them through the door before we welded it: 'sleep, and the wheel will keep the air; sleep, and the yellow lights are there; sleep, for the warden on the stair will hold, will hold, will hold.'"),
        new LoreFragment(135, "A Reason",
            "Why keep records at all, in a dying place? Because a thing that is remembered is not wholly ended, and we are not yet willing to wholly end, and ink is cheap, and defiance must take the forms available to it."),
        new LoreFragment(136, "Indenture of a Prentice",
            "Know all men that one Tomas, of no great family, bindeth himself this day prentice to the Hazard Division for seven yeares, to fetch and to carry and to learn the keeping of valves and the reading of lanternes, in return for his bread, his bed, and such breath as the Core can afforde him."),
        new LoreFragment(137, "Proclamation, Nailed to the Gate",
            "By the Master's command: let no soul descend below the third gallerie, the which gallerie is now the propertie of the deepe; and the deepe keepeth what it taketh, and giveth back only walking griefe. So sworn, so posted — so ignored by the desperate, God keep them."),
        new LoreFragment(138, "A Receipt for Refining (incomplete)",
            "Take of the raw humour one measure; subject it to the centrifuge at the appointed spin; draw off the bright phlogiston; and on no account permit the dark residuum to — (the receipt ends; the next word, in every surviving copy, has been scratched out by a frightened hand)"),
        new LoreFragment(139, "The Three Lamps Riddle",
            "At the deep junction stand three lamps and two corridors. One lamp lies. To pass: trust the corridor that BOTH true lamps favour, never the one a single lamp insists upon. The single insistence is how it calls you. Many have been called."),
        new LoreFragment(140, "On Echoes",
            "Shout in the deep galleries and the echo comes back a half-beat late and a half-tone wrong, as though something repeated it on purpose, getting it almost right — learning. Do not shout in the deep galleries. Do not teach it your voice."),
        new LoreFragment(141, "The Map That Updates Itself",
            "We keep one master map, chained to the table. Twice now it has shown a corridor none of us drew, in fresh ink, going down. We have not taken that corridor. The ink does not fade. We have stopped looking at the lower third of the map."),
        new LoreFragment(142, "Warden Field Manual, Page 40",
            "The fortieth and final rule, added in a later hand beneath the first thirty-nine: 'Rule forty: there is no winning here. There is only the length of the holding. Make it long. Make it kind. Make it count. That is the whole of the craft. Hold.'"),
        new LoreFragment(143, "Scrawl, Resin Store",
            "count the crates twice. the tide counts them once, and prefers its own count, and adjusts."),
        new LoreFragment(144, "The Tax Collector's Last Round",
            "Came for the Guild's tithe. Found the tithe-house open, the strongbox full, the clerk gone. Took nothing. Coin buys bread; there is no bread; therefore coin is now only heavy. I left it for the tide. Let IT pay tax for once."),
        new LoreFragment(145, "The Glassblower",
            "I made the observation ports — the sealed ones that look into the deep. I made them too well. So clear that men forget there is anything between themselves and what they watch, and lean in, and that is my fault. I have learned to make the new glass cloudy on purpose. A kindness, frosted in."),
        new LoreFragment(146, "The Drummer Boy",
            "Too young for the line, they said, so they gave me a drum, to beat the cadence the barricade-crews haul to. I beat it through three breaches. I am not too young for anything now. The drum still sounds the same as the day they gave it to me. I do not."),
        new LoreFragment(147, "On the Feral, a Kinder Note",
            "One of the diggers brought me a stone today — laid it at the barricade and fled. A grey stone, smoothed by a hand that remembered, once, the shape of giving. I kept the stone. I have not told the others. Some mercies must be carried quietly."),
        new LoreFragment(148, "Scrawl, Forge Quarter",
            "it is one hundred and eighty degrees here and i am shivering. write that down. someone please write that down and tell me it is the heat."),
        new LoreFragment(149, "The Astronomer's Complaint",
            "I cannot read the stars; the Core's exhaust has lit the whole sky a dull amber, dawn to dawn. We have traded the heavens for a furnace. I keep my charts anyway, of a sky I remember, and pretend the amber is only a long, long sunset — and that something will rise after it."),
        new LoreFragment(150, "Recipe, Canteen Margin",
            "barley, two measures. water, what you can spare. salt, a pinch, if Voss has not hidden it again. boil. serve to whoever is still standing. it is not good. it is warm. warm is the new good."),
        new LoreFragment(151, "The Locksmith's Confession",
            "I can open any door in the kingdom. I have therefore taken great care to learn which doors must never be opened, and to forget, on purpose, how. There is a satisfaction in unlearning a skill for love of the people it would have killed."),
        new LoreFragment(152, "Scrawl, Deepest Reached Point",
            "this is as far as we go. past here the lanterns will not stay lit and the map will not stay drawn and the men will not stay men. we have marked it with a line of yellow paint. do not cross the yellow. the yellow is the kingdom now."),
        new LoreFragment(153, "The Widow's Lantern",
            "She lights one at the seal every dusk for a husband three years past finding. We have told her, gently, all the gentle things. She lights it still. 'He followed yellow lights all his life,' she says. 'He would not know how to come home to anything else.' We have stopped telling her gentle things. We help her light it."),
        new LoreFragment(154, "On the Sound of the Core",
            "Strangers think it a roar. Live with it and you hear the parts: the eight true wheels, the hiss of the clean vent, the deep slow knock that no one will name. You stop hearing the roar. You never stop hearing the knock. You learn to call it a heartbeat. You learn not to ask whose."),
        new LoreFragment(155, "The Painter",
            "I painted the Master Artificer's portrait in the good years — all brass and confidence. I paint now on bulkheads, in scrounged pigment: the wheel, the lantern, the warden on the stair. Small icons for a small faith. The portrait is somewhere under the tide. The icons, at least, are looked at."),
        new LoreFragment(156, "Scrawl, Infirmary Wall",
            "they ask to go down to the seam. do not let the asking become reasonable to you. the day it sounds reasonable, you are sick. go to the deck. tell someone. do not go down."),
        new LoreFragment(157, "The Last Schoolmaster",
            "I teach the nine children of Deck One their letters, that they may read the warnings, and their sums, that they may count the lanterns and the crates and the days. I do not teach them the history. There will be time for grief when there is time for anything. For now: letters, sums, and the nearest sealed door."),
        new LoreFragment(158, "A Gambler's Creed",
            "Everything down here is a wager: which corridor, which lamp, which morning. I was a gambler in the good years and lost my shirt; I am a gambler now and win each day I keep my skin. The stakes improved my game. I do not recommend the method."),
        new LoreFragment(159, "On Hope, Practically",
            "Hope is not a feeling here; it is a chore, like priming the manifold or counting the resin. You do it whether or not you feel it, because the doing keeps the thing alive, and the thing, fed daily, occasionally feeds you back."),
        new LoreFragment(160, "The Cartographer, Later",
            "I have drawn my last map. It is blank but for the Core at the centre and a single yellow ring around it, and one word inside the ring: HERE. Everything outside the ring is now, accurately, unknown. It is the truest map I have ever made. It is also the smallest."),
        new LoreFragment(161, "Scrawl, Beside the Ninth Wheel",
            "do not turn it. i know you are curious. curiosity is how the door was opened the first time. do not turn it. let it be still. please. — every hand that has stood here, in turn, since the founding pour"),
        new LoreFragment(162, "The Tinker's Marvels",
            "I mend the little clockwork the children play with — the tin wardens, the spinning tops. It is the only work left that ends in laughter instead of a lower count. It may be the most important work left in the kingdom. No one has argued. No one has the heart."),
        new LoreFragment(163, "A Soldier's Plain Account",
            "I am not eloquent. We hold the line. The line breaks. We make a new line behind it, closer to the Core. We have made many lines. Each is shorter than the last, and easier to defend, which is the only good thing about losing ground: less of it to lose, next time. We will hold this one too. Until we make the next."),
        new LoreFragment(164, "The Last Wedding",
            "They married on Deck One, by the bean-garden, under the lamp — the chaplain doubting, the bride radiant, the whole nine of us for guests. We gave them resin for a dowry and a sealed door for a home. It was the happiest hour of the year. We did not let anyone mention the tide. The tide can wait one hour. Even the tide."),
        new LoreFragment(165, "On the Names That Come Back",
            "We painted over the wall of self-written names a fourth time today. While the paint was wet, a name appeared in it. We let it dry around the name. We have decided: if the dead wish to be remembered badly enough to write themselves into our walls, we will let them, and read the names at the dawn handover, and call it an honour roll instead of a haunting. The choice of word is the only power we have left."),
        new LoreFragment(166, "The Vintner's Sorrow",
            "My cellars are under the tide — good years, all of them, drowned in purple that is not wine. I saved one bottle. I will open it when the Core stops, or when we win, whichever comes first. It is, I am increasingly certain, a bottle I shall never open. I find I am at peace with this. The saving was the point."),
        new LoreFragment(167, "Scrawl, Pump Room, Later",
            "it has finished counting. nothing happened. we were braced for something and nothing happened and that is so much worse. it has started counting again. higher this time."),
        new LoreFragment(168, "The Archivist's Purpose",
            "I gather these scraps — the logs, the letters, the scrawls, the lies the Guild told and the truths the clerks hid — and I bind them, that whoever comes after, if anyone comes after, will know we were here, that we worked, that we held, that we loved in the warm dark while the tide rose, and that we wrote it down. Read them. That is all I ask. We wrote it down for you. Read them."),
        new LoreFragment(169, "The Foreign Mapmaker's Reply",
            "Your envoy asked for mapmakers. I came. I cannot map your kingdom; it will not hold still. But I can tell you what I have mapped: the road you walked to bring me here is already closed behind us. Wherever this ends, it ends with us inside it. I have, at least, drawn that. It is a short map."),
        new LoreFragment(170, "A Child's Question, Recorded",
            "The smallest of the Deck One children asked the schoolmaster: 'when the air is all good again, can we go up?' The schoolmaster said yes. The schoolmaster wrote, afterward, in this very record: 'I lied to a child today, and I would do it again, and I would do it a thousand times, and it is the only lie I have ever been proud of.'"),
        new LoreFragment(171, "On the Yellow",
            "Why yellow, the prentice asked — for the safe lights, the safe lines, the paint that marks the edge of the kingdom? The old hand thought a while. 'Because it is the colour of a lamp in a window,' she said. 'Because it is the colour of someone waiting up for you. We did not choose it. We remembered it.'"),
        new LoreFragment(172, "The Penultimate Page",
            "If you have read this far — archivist to archivist, reader to writer — then the binding held and the ink held and the kingdom held long enough to be remembered, and that is a victory the Guild with all its brass never managed. Close the book gently. Light a yellow one. Then go and hold whatever you have. There is nothing else to it. There never was."),
        new LoreFragment(173, "The Last Fragment",
            "Here the records thin to nothing. Beyond this, only the wheel turning, the lanterns drifting, the warden on the stair. If you are the one who comes after — the air is yours now. Keep it good. Someone kept it good for you, in the dark, and asked only that you do the same, and did not wait to be thanked. Hold. Hold. Hold."),
    };
}


//  EXTRA LORE (batch 2) 

public static partial class LoreContent
{
    internal static readonly LoreFragment[] More2 =
    {
        new LoreFragment(174, "A Wife's First Sign",
            "He came up from the deep seam, kissed my brow, and called me by my mother's name — who is forty years dead. I laughed. He laughed. We have not laughed since. The calling-by-wrong-names was the first sign. I wish someone had told me it was a sign. I thought it was tiredness. It is never tiredness."),
        new LoreFragment(175, "A Wife's Journal, Later",
            "He no longer sleeps. He sits at the table and works the same sum on the cloth with his finger, over and over, and when I ask what he reckons he says, gently, 'how many of us there ought to be' — and the answer he reaches is always smaller than the number at the table, and he counts me last."),
        new LoreFragment(176, "A Wife's Last Entry",
            "I have barred the bedroom door from my side, which is the wrong side, I know. He stands outside it and recites figures in a kind voice and asks to be let in to 'correct' me. I love him. I loved him. I will keep it barred. If you find this: do not open the door for the kind voice. The kindness is the worst part."),
        new LoreFragment(177, "The Husband, Before",
            "(a man's hand, still steady) If I start to count, take the chalk from me. If I call you by the wrong name, that is the sludge speaking, not your husband. I write this while I am still the one writing. Bar the door. Do not grieve the thing that wears my face — grieve me now, while I can be grieved. I would rather be wept for once, by you, than a hundred times by strangers."),
        new LoreFragment(178, "The Baroness's Complaint",
            "The servants have all gone feral or fled, which amounts to the same in terms of the service one receives. I am told the kingdom is ending. I have resolved to end with it correctly: in good linen, at the proper hour, having dressed for it. One does not abandon standards merely because the world has."),
        new LoreFragment(179, "The Baron's Cellar",
            "My lord buried his gold beneath the east wing the night of the Spill, that the tide should not have it. The tide took the east wing, gold and all, and my lord besides, who would not leave the digging. I, his steward, log this: he died richer than any man in Oakhaven, and it bought him precisely one extra hour of shovelling."),
        new LoreFragment(180, "An Aristocrat's Conversion",
            "I was a man of property and contempt. I owned three streets and pitied no one on them. The tide owns the three streets now, and I share a sealed room with a cook, a smith, and a child, and I have learned their names, and I would die for the child — and I do not recognise the man who wrote the first sentence. The tide took everything. I am, absurdly, grateful for the one thing it left."),
        new LoreFragment(181, "The Technician's Checklist",
            "Coolant: primed. Bearings: greased. Wheel-count: eight, as it should be. Knock from below: present, as always — do not investigate, see standing order. Mood of the night crew: poor. Mood of the day crew: poorer. Self: functional. 'Functional' is the highest grade I award anyone now, including the Core, including me."),
        new LoreFragment(182, "The Technician Who Listened",
            "Against orders, I put my ear to the ninth wheel's housing. I will not write what I heard. I will only write that I have re-welded the housing, added three plates, that I no longer eat alone, and that I check the welds each dawn — and the welds are fine, the welds are always fine, and I check them anyway."),
        new LoreFragment(183, "A Priest's Inventory of Sins",
            "We confess, in these last days, smaller sins and stranger ones: I hoarded a candle. I lied to a child about the air. I felt relief when a neighbour's lantern went dark, for it meant more rations. I absolve them all. God — if He is anywhere left in this place, it is in the clean steam — will understand. We were not built for this either. None of us were."),
        new LoreFragment(184, "The Priest's Apostasy",
            "I have stopped asking the old God for rescue and started asking the Core for time, and I cannot say whether the change is heresy or merely honesty. The Core, at least, answers — turning, keeping the air — which is more than the old God managed for the three hundred at the long tables. Judge me for it. Let me be judged. I kept people breathing. Judge that."),
        new LoreFragment(185, "Regular Folk, a Census",
            "Who is left, on Deck One: a cook, a smith, a locksmith, a schoolmaster, a bell-ringer, a doubting chaplain, a baroness who does not doubt, nine children, four wardens, and one beekeeper with no bees. We are not heroes. We are a Tuesday's worth of ordinary people the tide forgot to take. It will have to be enough. So far, it is."),
        new LoreFragment(186, "The Washerwoman's Note",
            "I scrub the wardens' suits between watches — the heavy lead-lined ones that smell of the deep. It is honest work; I sing while I do it. The wardens say my singing is the second-best sound in the kingdom, after the wheels. Nobody has told me what the worst one is. Nobody has had to."),
        new LoreFragment(187, "The Old Soldier's Comfort",
            "Survived three wars before this. The young ones ask how. I tell them: a war ends, lad — that is the trick of it, you need only outlast it. Then I do not tell them the rest: that this is not a war, and may not end, and that 'outlast' is a word I no longer fully trust. Let them have the trick. The trick is most of courage anyway."),
        new LoreFragment(188, "A Child's Brave Lie",
            "The smallest one tells the others there are knights coming from the east to save us. There are no knights; the east is closed by weather that is not weather. But the others sleep better for the knights, and the smallest one does not sleep at all, keeping watch for them — That child keeps the loneliest watch in the kingdom, every night, for knights that are not coming. I give them the extra ration and say it is for growing."),
        new LoreFragment(189, "The Drunkard's Clarity",
            "Sober now, perforce — the cellars are drowned. And sober, I see it plain, what the wine hid: we did this. Not the deep, not the tide, not the Gunge. We dug the door and called the digging progress and drank to it. I drank to it loudest. I am sober now. It is the worst gift the tide has given me, and I cannot give it back."),
        new LoreFragment(190, "The Midwife's Record",
            "Three born since the Spill, all in the sealed crèche, none of whom I was let in to attend — only to call instructions through the welded door. Three cries I heard, and answered with old words through cold metal. I do not know their faces. I know they live, because the door is quiet in the way of a place where someone is cared for, not the other quiet. I cling to the difference."),
        new LoreFragment(191, "The Clockmaker's Heresy",
            "Time runs wrong here — fast in the frost-quarter, slow in the forge — and my clocks, my beautiful clocks, all disagree. I have stopped trying to make them agree. I have set every clock in the kingdom to the dawn-watch bell instead. We do not keep the hour anymore; we keep each other's company in the same wrong hour — which is what a clock was for all along, though it took the death of the hours to teach me."),
        new LoreFragment(192, "The Mapmaker's Apprentice",
            "My master drew the kingdom; I draw only the safe corridors now, in chalk, and rub them out when they betray us. He calls it a lesser craft. I say a map you can trust your life to is worth more than a map you can frame. He has stopped arguing. Yesterday he asked to borrow my chalk."),
        new LoreFragment(193, "The Glassblower's Worse Confession",
            "I frosted the deep observation ports, as I confessed before. I have since done worse: walled one over entirely, in brick, against orders, because a warden had begun to spend his off-watch hours before it, not looking away. He is angry with me. He is also still himself. I will take the anger. The anger talks like a man. What was forming behind that glass did not."),
        new LoreFragment(194, "Scrawl, Officer's Quarters",
            "promoted to command the deck today. there is no deck left to command, only nine people and a machine. i have decided command means making sure the nine eat and the machine turns. i am, it turns out, good at this. i was a terrible officer in the good years. funny, what the end of the world qualifies a man for."),
        new LoreFragment(195, "The Tax Ledger, Annotated",
            "Final entry, in the assessor's tidy hand: 'Assessed value of the kingdom of Oakhaven — incalculable. Collectible value — nil. The two were always different numbers. I spent my life on the first. I should have spent it on the second. There is no second life in which to correct this. File under: too late.'"),
        new LoreFragment(196, "A Letter to a Brother, Abroad",
            "Willem — do not come home, whatever the rumours say. There is no home to come to, only a machine and the people who feed it. Stay where the sky can be trusted. Marry the girl. Name a son for me if you must, but tell him I was a clerk who kept good records, not a hero — the kingdom has heroes enough now, and not one of them wanted the job. Be happy. That is the whole of the will I leave you."),
        new LoreFragment(197, "The Cook's Second Lament",
            "I have learned to make nine portions taste like a feast through sheer lying with spice. The baroness says I have a gift. I tell her hunger is the gift; I merely garnish it. We laughed — a baroness and a cook, over barley, at the end of the world. The old kingdom would not have permitted it. The new kingdom is built of nothing else."),
        new LoreFragment(198, "The Astronomer's Last Chart",
            "I have charted the amber sky for a year and learned one thing: it does not change. No dawn, no dusk, no wheeling stars — only the Core's breath, lit from within, the same dull glow forever. I have folded my instruments. There is nothing up there to measure. I asked the schoolmaster if I might teach the children sums instead. He wept and said yes. I had forgotten people could be glad of me."),
        new LoreFragment(199, "The Smith's Daughter",
            "Father makes barricades; I carry the rivets. I am eleven. I can name every plate by its ring and tell a good weld from a bad by the colour. Other children, before, learned letters and dancing. I learned iron — and I am not sad about it; iron has kept me alive and letters would not have. But sometimes I draw a dancing girl on the plates in chalk before father seals them into the wall, so that somewhere, sealed up, a girl is always dancing."),
        new LoreFragment(200, "The Hundredth Log",
            "Whoever you are, reading the hundredth thing we thought to write down: we did not expect to need a hundred. We expected to be saved, or ended, long before the record grew so long. Instead we endured, and the enduring filled a hundred pages — and that is its own strange victory: a kingdom that wrote a hundred logs while drowning, each one beginning, somewhere in its heart, with the word 'still.'"),
        new LoreFragment(201, "Scrawl, Frost Quarter",
            "left my glove off for one minute to write this. the cold here is not cold. cold is honest. this is patient. there is a difference, and now my hand knows it too."),
        new LoreFragment(202, "The Locksmith's Daughter",
            "Father forgets, on purpose, how to open the bad doors, and is proud of forgetting. I am learning the same forgetting. It is harder than learning. To make your clever hands stupid for love — that is a craft they teach in no guild. We practise it together, evenings, unlearning. It is the closest thing to play we have left."),
        new LoreFragment(203, "The Envoy's Final Dispatch",
            "To my court, last sending: I will not return. The road is closed and, besides, I have come to love these stubborn, doomed, ordinary people more than I love being safe. Send no rescue; it cannot arrive. Send instead, into your histories, one true line: that a kingdom called Oakhaven kept the air good for one another long after hope was a reasonable thing to keep. Let that be the war I died reporting."),
        new LoreFragment(204, "The Marriage Rite, Amended",
            "I have rewritten the vows for the times. No longer 'till death do you part' — death is too small a word now, and too uncertain. Instead: 'until the air fails, or the wheel stops, or the tide takes the both of you in the same hour' — which is the only good luck left to wish a couple. I married two of them by it last week. They wept. So did I. It is, God help me, a better vow than the old one."),
        new LoreFragment(205, "The Stargazer's Child",
            "My father charts a sky I have never seen — a blue one, with a yellow sun, not the amber. He says it is real and only hidden. I believe him, because he is my father, the way I believe in the knights from the east. We are a family of believers in things behind the amber. It is, my mother says, an inheritance. It is, I think, the only one worth having."),
        new LoreFragment(206, "A Madman's Lucid Hour",
            "They keep me chained, for the safety of the rest, which is correct, for I am not always myself. But in the lucid hours — fewer now — I am clearer than I ever was whole. The Gunge does not lie, you see. It only counts. And in the counting I have glimpsed the shape of the sum, and the shape is mercy of a terrible kind: it wants to make us all still, and stillness, it insists, does not hurt. Do not believe it. The wanting is the hurt. Re-chain me. The hour is ending. I can feel the numbers getting friendly again."),
        new LoreFragment(207, "The Quartermaster's Riddle",
            "Posted over the stores: 'I am spent to be kept and kept to be spent, and the day you have plenty of me is the day you should be most afraid.' The answer, scratched below by some wit: 'TIME, you grim old goat — now open the stores, we are hungry.' Both hands, I am told, are dead now. The riddle remains. So does the hunger."),
        new LoreFragment(208, "The Carpenter's Coffins",
            "I made fine furniture once; I make boxes now, plain ones, and I have stopped putting names on them because the names stopped staying put. A box is a box. A man deserves better than a box, but better is not on the manifest, and so I plane each one as smooth as I can, and that smoothing is the whole of the honour I have left to give. I have given it three hundred times. My hands are very good at honour now."),
        new LoreFragment(209, "Scrawl, Deepest Stair",
            "i went one flight too far. i am writing this on the way back up, fast, do not stop to read it, GO. if you are reading this slowly you have already stayed too long. the stair counts your steps down and forgets to count them back up. GO."),
        new LoreFragment(210, "The Beekeeper, Later",
            "I keep an empty hive on Deck One, by the bean-garden, in case the bees come back. The others think me soft. But the bees knew, before any of us, which way safety lay — they flew east, into the trusted sky — and if anything is ever coming back to save this world, it will be small, and wise, and it will arrive at a clean flower. So I keep the hive ready. So I keep one flower clean. It is a small faith. I will keep telling you that."),
        new LoreFragment(211, "The Aristocrat's Daughter, on Watch",
            "Mother dresses for the end; I have taken a warden's watch instead. She calls it beneath me. It is, in fact, the first thing I have ever done that was above me — and I find I can do it, and the doing has shown the dressing-for-the-end to be the small, frightened thing it always was. I love my mother. I will not become her. The tide has drowned at least that future, and I thank it for the one mercy."),
        new LoreFragment(212, "A Note Passed Through the Seal",
            "Pushed under the welded crèche door, in a child's careful print, answering our weekly lantern: 'we see the light. we are seven and we are not afraid because the singing comes through the door. tell the singer thank you. tell the singer we sing back — can you hear? sing again tomorrow. we will be here.' We have read it aloud at every dawn handover since. Seven children are holding the whole kingdom up through a welded door, and do not know it, and must never be told the weight."),
        new LoreFragment(213, "The Cynic's Surrender",
            "I mocked the lantern code, the dawn bells, the chalked icons — the whole sentimental machinery of hope — for a year. Then I caught myself lighting a yellow one, unbidden, for no one, and weeping, and I have stopped mocking. Cynicism is a fine coat for fair weather. It is no use at all against a patient tide. I have hung it up. I light the lanterns now. Mock me. I have earned it. I do not care."),
        new LoreFragment(214, "The Surveyor's Final Stake",
            "Drove the last survey stake today — not to measure but to mark: the new edge of the holdable kingdom, a yellow ring round the Core, smaller than last month by forty paces. I no longer record the shrinking with despair. I record it the way a man records his own grey hairs — as proof he is still here to count them. We are smaller. We are here. Both true. Tomorrow I may move the stake. Today it holds."),
        new LoreFragment(215, "The Last Festival",
            "We held the Lantern Festival on the old date, because the date is ours even if the season is not — nine children, four wardens, a baroness, a cook, and the rest, on Deck One, under the lamps, with barley-cake the cook conjured from nearly nothing. We sang the centrifuge hymn with the new words. For one night the tide was just the dark beyond the lamps, and we have never been afraid of dark. We were afraid of patience. For one night, we forgot to be."),
        new LoreFragment(216, "Scrawl, Behind the Turret Bank",
            "fed the turrets all the resin we had. they ran hot and true and the tide pulled back a whole gallery. for an afternoon we were WINNING. write that down. for one whole afternoon, in the warm dark, at the end of the world, we were winning, and we knew it, and we cheered. it did not last. write it down anyway. we were winning once, and no tide can make that not have happened."),
        new LoreFragment(217, "The Old Foreman's Wisdom",
            "Voss is gone, but he left this, chalked where the shift-crews gather: 'count your people every dawn. when the count holds, that is a victory. when it drops, mourn fast and count again at dusk. do not stop counting. the day you stop counting is the day you have decided they do not matter — and they matter, they are the only thing that ever mattered. count them.' He chalked that in the good years. The morning his own count came out wrong, he could not follow it — he put down the chalk and walked out and was not seen again. Count anyway. Count especially then."),
        new LoreFragment(218, "A Wife's Reconciliation",
            "I barred the door against the kind voice three seasons ago and did not open it again. The voice long ago went quiet. Yesterday I unbarred it and went in. There was only dust, and his good coat, and his chalk, and a wall covered floor to ceiling in a single sum worked over and over, the number always one short. I have decided the number it was short was me — that even mad, even at the end, he was trying to make the total come out with me still in it. I have taken the coat. I light a yellow one. I am in the total. He saw to that."),
        new LoreFragment(219, "The Schoolmaster's Curriculum",
            "Letters, sums, the lantern code, the location of every sealed door — and, added this week against my own first judgment, the names of the dead, and what each did, and why it mattered. They must know letters to survive. They must know the dead to be worth surviving. I had thought to spare them the grief. I was wrong. A child who knows whose air they breathe holds it more carefully. We do the names at dusk. They are better at it than I am."),
        new LoreFragment(220, "The Final Inventory",
            "Last count, all decks: people, fourteen; lanterns, two hundred-odd; resin, eleven crates; hope, unmeasurable but present; time, unknown but ongoing; the Core, turning. The clerk has added, beneath the tally, in a hand that does not shake: 'a kingdom is not its acres. it is its count of the living, multiplied by their care for one another. by that arithmetic we are, even here, wealthy beyond the old kings. close the ledger. go and be wealthy.'"),
        new LoreFragment(221, "Scrawl, Canteen",
            "twelve bowls. nine of us. the cook still sets out twelve. nobody says anything. we eat around the empty three like they are guests. they are guests. they are the best-behaved guests we have."),
        new LoreFragment(222, "The Wheelwright, at the End",
            "Thirty years I swore the wheels would never fail, and they have not — eight of nine turn yet, true as the day I shod them. If this kingdom ends, let the record show the wheels did not lose it for us. The wheels did their part. We are the part that is uncertain. They shame us with their constancy. So I let them shame me, each morning, into doing mine."),
        new LoreFragment(223, "A Prayer Found in a Suit Pocket",
            "Not to any god named: 'whatever keeps the air, keep it one more day. whatever holds the line, hold it one more hour. whatever turns the wheel, turn. i ask for nothing for myself; i have stopped being a self. i am a hand on a valve. let the valve hold. amen, or whatever word means please.'"),
        new LoreFragment(224, "The Doctor's Triage Note",
            "I have three kinds of patient now: those the honey and calm can keep; those the seam is calling, whom we must bar from the stairs; and those past both, who ask only to be remembered. I can treat the first, restrain the second, and listen to the third. Three kinds of mercy. I dispense all three. I have run out of everything but mercy. It is the one supply that does not deplete. I do not understand this. I do not question it. I dispense."),
        new LoreFragment(225, "Scrawl, Above a Sealed Door",
            "what is behind here loved us once. remember the loved-us. do not open the door. you seal a door against a stranger out of fear; you seal it against your own out of grief. this door is grief. let it stay shut. let them stay loved."),
        new LoreFragment(226, "The Tinker's Final Marvel",
            "I have built the children a little theatre of tin — wardens, gremlins, a spinning Core, a tide of painted cloth you can crank back with a handle. They play out our whole war on it nightly, and in their version we always win, because the handle turns both ways and a child's hand is on it. I think it is the truest history of the kingdom we have: the one in which the tide can be cranked back. Let theirs be the version that survives."),
        new LoreFragment(227, "An Aristocrat's Will",
            "I, who owned three streets, leave them to the tide, which has them anyway. I leave my title to no one; titles are weather now. I leave my good linen to the cook, who has fed me without once curtsying, and earned it. I leave my apology to the streets' former tenants, whom I never saw as people and who turned out to be the only people there were. I leave, lastly, my thanks to the child who taught me my name means nothing and her friendship means everything. The estate is settled. I am, finally, solvent."),
        new LoreFragment(228, "The Bell-Ringer's Confession",
            "I rang the seal-ring once — the forbidden one — by accident, my tired hand slipping. The whole kingdom froze; nine children stopped breathing. Then I rang three fast, for dawn, to undo it, and two for dusk though it was not dusk, and rang the hours all wrong all day to drown the one. No one scolded me. They understood. We do not ring one. Especially not by accident. I tie my tired hand to my side at night now. The bells are too honest to trust to a tired hand."),
        new LoreFragment(229, "Scrawl, Resin Vault",
            "the violet stones hum if you hold enough of them. the diggers used to hum the same note, down in the seam, before. i have stopped holding enough at once. i do not want to learn that song. some of the older hands already know it — you can tell, they hum without meaning to. do not hum back."),
        new LoreFragment(230, "The Coat, Given",
            "The coat I took from his room has gone to the children's quarters; it makes a fine blanket for three of the smaller ones. He would like that better than my keeping it folded in grief. The dead do not want monuments. They want their coats put to use, their air kept good, and their names said plainly at dusk. I say his plainly now. It does not break me. It warms three children. That is what a name is for, in the end. Use it like a coat."),
        new LoreFragment(231, "The Foreign Mapmaker's Last Map",
            "I came to map a kingdom and could not — it would not hold still. So I have mapped, instead, its people: here the cook, here the smith, here the nine children, here the wardens on their stairs, here the widow and her lanterns, here the baroness learning to laugh. A map of who, not where. It holds still. People hold still in a way land never did for me. It is the best map of my life, and the smallest, and it will never be framed in any court, and I do not care. I am on it — bottom corner. The mapmaker who stayed."),
        new LoreFragment(232, "Scrawl, the Warden's Post",
            "warden: the air behind you is real. the people breathing it are real. the tide before you is patient, but you are stubborn — and stubborn, kept up daily, looks a great deal like hope. hold the line. gather the resin. light the lanterns. read the logs we left you. we held it this long so you could hold it longer. now go. the wheel is turning. so are you."),
        new LoreFragment(233, "The Cook's Recipe for the End",
            "Take what little you have. Share it past the point of sense. Garnish hunger with company. Set out bowls for the dead, so the living are not alone. Serve it warm. Serve it laughing, if you can manage laughing. Serve it. The serving is the meal. I have cooked three hundred years' worth of meals in three short years and learned only this, and it is everything: serve it warm, and do not eat alone."),
        new LoreFragment(234, "The Last Apprentice",
            "Tomas, who bound himself seven years to the Hazard Division, has served three — and there are no masters left to learn from, so he teaches himself from the logs, and teaches the children from what he teaches himself, and signs his practice-sheets 'prentice still, out of stubbornness or hope. He will never make journeyman; there is no Guild to raise him. He keeps the valves anyway. The title was never the point. The keeping was the point. He keeps."),
        new LoreFragment(235, "Scrawl, the Final Corridor",
            "this is the last thing written in the kingdom that i know of. if there is writing past this, someone held longer than us, and that is the best news there could be. close the book. light a yellow one. hold whatever you have. the wheel is turning. that means there is still time. there is always, while the wheel turns, still time. still. still. still."),
    };
}

//  EXTRA LORE (batch 3) 
// Thread-closers and thread-openers. Ids 236-256. These deliberately cross-reference
// earlier fragments (Mira: 20/28/52; the ninth wheel & the knock: 73/74/154/161/182;
// the creche lantern: 48/147; the sealed stores: 132; "A. Lich": 38/131; the
// eleventh hour: 34/36; the Behemoth: 45; the count: 61/127/217). Keep ids stable.

public static partial class LoreContent
{
    internal static readonly LoreFragment[] More3 =
    {
        new LoreFragment(236, "Deck Log: The Woman from the East",
            "A woman came in off the eastern ridge at dusk, following the lanterns, carrying one of ours — burned out, weeks old, its treated strip long spent. She would not eat until she had asked after two names. The first name is behind the containment door. The second kept the south valves, and died at them the winter before last, with a seat kept dry at his table to the end. She said nothing for a long while after that. Then she asked where the lanterns were made, and whether the work wanted hands."),

        new LoreFragment(237, "The Lantern-Maker's Mark",
            "Every lantern out of the shop these three years carries the same small mark scratched into the brass collar: a chair. Four-legged, plain, no bigger than a thumbnail. I asked her once what it meant. She went on crimping the seam and said only that somebody once kept a seat for her longer than sense allowed, and that a light is a kind of seat — kept for whoever is still out there. I did not ask again. The rest of us mark our work with initials, like fools. Hers are the ones people follow home."),

        new LoreFragment(238, "M.'s Page, Left in the Lantern Shop",
            "Two letters were written to me. One said come home before the lanterns burn green; it was never sent, and he kept my seat dry until the winter took him at his valves. The other is pinned to a door I will not open, and asks me to light a yellow one, and to keep the watch. The watch still runs. I wind it at the dawn bell. Everything else in this kingdom stopped at the eleventh hour — not the watch, and not me, and not the lights. I make the lights now. If you are out in the waste following one in: it was lit for two men who cannot come to it. Come in their place. That is what it is for."),

        new LoreFragment(239, "Sinking Record, the First Shaft (old hand)",
            "Ano the seconde of the workes: at foure hundred foote the bore strucke no rock but a softnesse, and the softnesse strucke BACK — three knockes upon the bit, evenly spaced, as a man knockes who expecteth answer. The Master ordered the ninth winch stilled and its housing sealed, and wrote in the margin of this record: 'we shall not knocke againe.' We drilled on regardless, a mile to the east. It was, we told ourselves, a different door."),

        new LoreFragment(240, "Memorandum: On Superstition",
            "To the deck, from the pressure office. The 'knock' is a worn crown-bearing in the number four gallery, period thirty-one seconds, and I have the charts to prove it. The ninth wheel is stilled because its governor was miscast in the founding pour and would shake the housing apart — a fact recorded plainly in the commissioning ledger, which nobody reads because a sealed door tells a better story. I signed the deep survey under protest and I was right, and being right bought this kingdom nothing. But I will not watch the deck frighten itself to death with bedtime tales. Superstition is a corrosion like any other. — the surveyor who is still, for the record, right"),

        new LoreFragment(241, "Where the Lanterns Go",
            "I broke the standing rule and watched the crèche seal through dusk, from the gantry shadows, to learn what takes the weekly lantern. What takes it is a digger — one of the feral ones, small, quick, keeping to the dark the way they do. It lifted the lantern with both hands, the way you carry soup, and went down the old inspection run that ends — I have checked the schematics twice — at a vent grate above the crèche level. I have not told the deck. They would argue about it. I just leave the lantern earlier now, and better filled, and once, against every rule we have, I left two."),

        new LoreFragment(242, "The Last Argument of the Three Chairs",
            "I kept minutes I was ordered not to keep. The night before the containment sector was sealed, the Master Artificer, the Chief Engineer, and the Chief Alchemist argued four hours over the item that now sits in the deep stores. The Engineer said it would drop the whole deep gallery and end the tide at the seam. The Alchemist said the same charge would crack the Core's footing, and the tide would not so much be ended as inherited. The Master said nothing for a long time, then had it carried down, and the three of them signed the manifest, and underlined the entry, and did not name it. They agreed not to use it. Nobody thought to agree on when."),

        new LoreFragment(243, "Payroll Anomaly",
            "For the ledger: the overtime sheet continues to fill itself, as reported, in the hands of the dead. One signature belongs to no one dead. It belongs to no one living either. It is the joke name — the one Foreman Voss wanted found out, back when it was funny — and it logs its hours only on the nights the sky comes down over the south quarter. I have cross-checked four such nights. Four entries. I am a clerk; I record what the record shows. The record shows that something is billing us for the meteors. I have locked the drawer."),

        new LoreFragment(244, "The Back Row",
            "At the dawn handover we stand in two rows and count. For some weeks the back row has held one figure whom everyone supposes somebody else knows — helmeted, correct, still. When Voss's count came out one too many, all those years ago, he stopped counting. We have talked it over and chosen differently: we count, we get one too many, we log the count, and we do not turn around fast. Whatever stands with us at dawn stands the watch. That is more than some of the living managed."),

        new LoreFragment(245, "The Clockmaker's Inventory of the Stopped",
            "I gather every stopped clock and watch the salvage crews bring in — forty-one now. Here is what I have told no one but this page: every one of them reads the eleventh hour. The ones that stopped in the Spill, yes — but also the hall clock that ran two years after, and the gate watch that drowned only last spring. Whenever a clock in this kingdom dies, it dies at eleven, as if the hands spend all their remaining days only arriving there. There is one watch left that still runs. I no longer offer to service it. Some things keep going because nobody has told them what time it is."),

        new LoreFragment(246, "Requisition, Returned",
            "Requisition eighty-one, bearing grease, four drums: DENIED — stores cite priority of the turret banks. Resubmitted, with note that without grease the barricade sleds seize and the turret banks will shortly be defending scrap: DENIED, wrong form. Resubmitted on the right form: APPROVED, two drums, collect after inventory. There is a tide at the walls and I am filling in forms in triplicate. In a strange way I find it steadying. The forms, at least, are exactly as stupid as they were before the world ended."),

        new LoreFragment(247, "Found with a Cache",
            "You will judge me. Fine. Forty tins under my floor, taken one at a time over two years, and every mouth on this deck fed lighter for it. I watched them share and sing and set out bowls for the dead and I thought: fools, the tide does not sing back. I meant to outlast them all. (Below, in the salvage officer's hand:) Cache logged and returned to stores, all forty tins, unopened. He died of the same winter as everyone else — alone, one deck down from the singing. Entered into the record without comment. Except this one."),

        new LoreFragment(248, "A Transcription of the Hum",
            "The infirmary asked me — I had an ear for music, before — to set down the note the resin hums, and the sludge-touched hum, and the diggers hummed in the seam. It is not a note. It is a work song, slowed a hundredfold: the lower-seam round, the one about going down and coming up. They are all still singing it, at a speed no living throat could hold. Whatever the Gunge keeps of a man, it keeps the shift. Somewhere inside that long, long note, the diggers believe they are still on their way up — to cold stars and a warm meal. I gave the infirmary the transcription. I did not keep a copy. I hum it anyway now, some mornings, without meaning to. I have told no one."),

        new LoreFragment(249, "The Party That Followed the Wrong Yellow",
            "Six went east after a yellow light the telemetry could not account for. One came back — Warden Sel, suit scored, voice level. Her report, complete: the light kept the same distance no matter their pace; the ground beneath it had never been walked; and when at last it let them close, it was not a lantern. It was only a colour, held up at lantern height. Asked after the other five, she said they were still following it, and that they did not seem distressed, and that this was the worst part — and then she asked to be excused. The amendment to the lantern code was posted the next morning. Read it. Read it every morning."),

        new LoreFragment(250, "The Circles, Plotted",
            "I plotted the Behemoth's patrol from the sighting reports — eleven months of bearings the wardens brought in. The circles are closing: a few dozen paces with every pass, patient as everything else out there. And they are not centred on the Core, which was the fear. They are centred on the containment sector. The machine that reads every living thing as rock is circling the one man who is neither, drawing in, month on month. I do not know whether it is hunting him, or guarding him, or answering him. I have filed the chart under the heading I use when I do not want the deck to read a thing: ROUTINE."),

        new LoreFragment(251, "Two Corruptions, One Author",
            "A note for whoever studies this after me. In flesh, the Gunge counts — the alchemist at his sky, the husband at his table-sums, the liches with their patient pens. In machinery, it guards — the excavators at their dead claims, the Behemoth on its circles. Counting, and guarding. An auditor, and a sentry. I put this to the deck plainly and was told to get some sleep. But those are not two madnesses. Those are two halves of one occupation. Something is taking inventory, and something is standing watch over the stock — and no one has yet asked what the stock is being kept for."),

        new LoreFragment(252, "The Four Votes",
            "For the honesty of the record: the motion to stop tabling the spill failed four to three, and here is what became of the four. One drowned under the east wing with his gold. One signed the last decree swearing there was no spill, and died of the air like most. One fled east the week the weather closed the roads, and the roads have not opened. And one sits on Deck One now, sharing a sealed room with a cook and a child, and is, by every account including mine, a changed and decent man. The three who voted aye are dead too. The vote decided nothing. I record it because the record is how we told ourselves who we were."),

        new LoreFragment(253, "Grey Cloud, East Watch",
            "East watch reports, dusk: a grey cloud, low, moving west against the wind — against it — toward the lanterns. The beekeeper was sent for, and stood a long while at the rail, and would not speak, and then said 'bees,' once, and sat down on the deck like a dropped coat. The lantern code has nothing to say about bees. The amendment about false colours has nothing to say about bees. We have doubled the watch and left the beekeeper at the rail, and there is one flower on Deck One getting more attention tonight than the Core itself. Whatever is coming in from the east, it is coming toward our light. We have not decided which way to hope."),

        new LoreFragment(254, "Struck from the Archive (kept anyway)",
            "The first draft of the binding's preface, found crumpled behind the shelf: 'They dug because they were paid, and were paid because we bought, and we bought because it was cold and the fire was cheap. Do not let anyone tell you the kingdom fell to a monster. The monster came second. First came the invoice.' The fair copy on the shelf says something kinder. Both hands are mine. A shelf can hold two truths; I have filed this one where an honest reader would think to look — behind the comfortable one."),

        new LoreFragment(255, "A Question for the Purifier Deck",
            "Put to me by an apprentice, and I could not answer it, so I am writing it down to be rid of it. The Core drinks refined Gunge. The turrets eat its resin. The tide has had three years to drown one machine at the centre of a beaten kingdom — and instead it circles, and presses, and waits. Why does a flood wait? The apprentice's theory, offered with the terrible ease of the young: 'Maybe you don't drown the part of you that's eating.' I sent him back to his valves. I have not slept well since. Feed the turrets. Hold the line. Do not think about the mouth."),

        new LoreFragment(256, "Loose Page, Unplaceable",
            "the dawn count came out right this morning. exactly right. first time in three years — no one missing, no one extra. we stood in our two rows with the number hanging in the cold air and nobody said a word, and then the bell-ringer rang three for dawn, and we went to our posts. we are all afraid to count again tomorrow. we will count again tomorrow."),
    };
}


