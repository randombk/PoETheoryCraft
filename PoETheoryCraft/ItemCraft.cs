﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PoETheoryCraft.DataClasses;
using PoETheoryCraft.Utils;

namespace PoETheoryCraft
{

    //Represents one actual in-game item, made from a PoEBaseItemData template
    public class ItemCraft
    {
        public static int DefaultQuality = Properties.Settings.Default.ItemQuality;
        public string SourceData { get; }           //key to PoEBaseItemData this is derived from
        public ISet<string> LiveTags { get; }       //derived from template but can change during crafting
        public int ItemLevel { get; }
        private ItemRarity _rarity;
        public ItemRarity Rarity                    //always rename when rarity changes
        {
            get { return _rarity; }
            set
            {
                if (value != _rarity)
                {
                    _rarity = value;
                    UpdateName();
                }
            }
        }
        public IList<ModCraft> LiveMods { get; }
        public IList<ModCraft> LiveImplicits { get; }
        public string ItemName { get; private set; }
        private int _basequality;
        public int BaseQuality                      //clamp min and max; update mods if catalyst qual changed
        {
            get { return _basequality; } 
            set 
            {
                int cap = QualityType == null ? 30 : 20;
                value = Math.Max(Math.Min(value, cap), 0);
                if (value != _basequality)
                {
                    _basequality = value;
                    if (QualityType != null)
                    {
                        foreach (ModCraft mod in LiveMods)
                        {
                            UpdateModQuality(mod, QualityType);
                        }
                        foreach (ModCraft mod in LiveImplicits)
                        {
                            UpdateModQuality(mod, QualityType);
                        }
                    }
                }
            }
        }
        public string QualityType { get; set; } //null for normal qual, or the name of a catalyst, ex: "Imbued Catalyst"

        //cache return value for faster repeated calls as long as item hasn't changed
        public bool Modified { get; private set; } = true;
        private ItemProperties GetLivePropertiesCache;
        public ItemCraft(PoEBaseItemData data, int level = 100, ISet<ItemInfluence> influences = null)
        {
            SourceData = data.key;
            _basequality = DefaultQuality;
            QualityType = null;
            ItemLevel = level;
            Rarity = ItemRarity.Normal;
            ItemName = data.name;
            LiveTags = new HashSet<string>(data.tags);
            if (influences != null)
            {
                foreach (ItemInfluence inf in influences)
                {
                    string s = data.item_class_properties[EnumConverter.InfToTag(inf)];
                    if (s != null)
                        LiveTags.Add(s);
                }
            }
            LiveMods = new List<ModCraft>();
            LiveImplicits = new List<ModCraft>();
            foreach (string s in data.implicits)
            {
                AddImplicit(CraftingDatabase.AllMods[s]);
            }
        }
        //deep copy everything
        private ItemCraft(ItemCraft item)
        {
            SourceData = item.SourceData;
            _basequality = item.BaseQuality;
            QualityType = item.QualityType;
            LiveTags = new HashSet<string>(item.LiveTags);
            ItemLevel = item.ItemLevel;
            _rarity = item.Rarity;
            LiveMods = new List<ModCraft>();
            foreach (ModCraft m in item.LiveMods)
            {
                LiveMods.Add(m.Copy());
            }
            LiveImplicits = new List<ModCraft>();
            foreach (ModCraft m in item.LiveImplicits)
            {
                LiveImplicits.Add(m.Copy());
            }
            ItemName = item.ItemName;
        }
        public ItemCraft Copy()
        {
            return new ItemCraft(this);
        }
        public int ModCountByType(string type, bool lockedonly = false)
        {
            int count = 0;
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (modtemplate.generation_type == type && (!lockedonly || m.IsLocked))
                    count++;
            }
            return count;
        }
        public int GetAffixLimit(bool ignorerarity = false)
        {
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            if (ignorerarity)
            {
                return itemtemplate.item_class.Contains("Jewel") ? 2 : 3;
            }
            switch (Rarity)
            {
                case ItemRarity.Rare:
                    return itemtemplate.item_class.Contains("Jewel") ? 2 : 3;
                case ItemRarity.Magic:
                    return 1;
                default:
                    return 0;
            }
        }
        public ISet<ItemInfluence> GetInfluences()
        {
            ISet<ItemInfluence> infs = new HashSet<ItemInfluence>();
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            foreach (ItemInfluence inf in Enum.GetValues(typeof(ItemInfluence)))
            {
                if (LiveTags.Contains(itemtemplate.item_class_properties[EnumConverter.InfToTag(inf)]))
                    infs.Add(inf);
            }
            return infs;
        }
        public bool RerollImplicits()
        {
            if (LiveImplicits.Count == 0)
                return false;
            foreach (ModCraft m in LiveImplicits)
            {
                m.Reroll();
            }
            Modified = true;
            return true;
        }
        //divines each mod, obeying "of prefixes" and "of suffixes" metamods and locked mods
        public bool RerollExplicits()
        {
            bool prefixlock = false;
            bool suffixlock = false;
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (modtemplate.key == ModLogic.PrefixLock)
                    prefixlock = true;
                if (modtemplate.key == ModLogic.SuffixLock)
                    suffixlock = true;
            }
            bool valid = false;
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (!m.IsLocked && !(prefixlock && modtemplate.generation_type == ModLogic.Prefix) && !(suffixlock && modtemplate.generation_type == ModLogic.Suffix))
                {
                    m.Reroll();
                    valid = true;
                }
            }
            if (valid)
                Modified = true;
            return valid;
        }
        //removes one mod at random, obeying prefix/suffix lock, and leaving locked mods
        public bool RemoveRandomMod()
        {
            bool prefixlock = false;
            bool suffixlock = false;
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (modtemplate.key == ModLogic.PrefixLock)
                    prefixlock = true;
                if (modtemplate.key == ModLogic.SuffixLock)
                    suffixlock = true;
            }
            IList<ModCraft> choppingblock = new List<ModCraft>();
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (!m.IsLocked && !(prefixlock && modtemplate.generation_type == ModLogic.Prefix) && !(suffixlock && modtemplate.generation_type == ModLogic.Suffix))
                    choppingblock.Add(m);
            }
            if (choppingblock.Count > 0)
            {
                int n = RNG.Gen.Next(choppingblock.Count);
                LiveMods.Remove(choppingblock[n]);
                Modified = true;
                return true;
            }
            else
                return false;
            
        }
        //remove all mods or all crafted mods, obeying prefix/suffix lock, leaving locked mods, and downgrading rarity if necessary
        public void ClearMods(bool craftedonly = false)
        {
            bool prefixlock = false;
            bool suffixlock = false;
            foreach (ModCraft m in LiveMods)
            {
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (modtemplate.key == ModLogic.PrefixLock)
                    prefixlock = true;
                if (modtemplate.key == ModLogic.SuffixLock)
                    suffixlock = true;
            }
            for (int i = LiveMods.Count - 1; i >= 0; i--)
            {
                ModCraft m = LiveMods[i];
                PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                if (!m.IsLocked && !(prefixlock && modtemplate.generation_type == ModLogic.Prefix) && !(suffixlock && modtemplate.generation_type == ModLogic.Suffix) && !(craftedonly && modtemplate.domain != "crafted"))
                    RemoveModAt(i);
            }
            ItemRarity r = craftedonly ? Rarity : GetMinimumRarity();
            if (r < Rarity)     //downgrade rarity if needed, which triggers name change
                Rarity = r;
            else if (Rarity == ItemRarity.Magic)     //magic items names are mod-sensitive, so force update
                UpdateName();
            Modified = true;
        }
        public void AddMod(PoEModData data)
        {
            ModCraft m = new ModCraft(data);
            LiveMods.Add(m);
            LiveTags.UnionWith(data.adds_tags);
            UpdateModQuality(m, QualityType);
            ItemRarity r = GetMinimumRarity();
            if (r > Rarity)     //upgrade rarity if needed, which triggers name change
                Rarity = r;
            else if (Rarity == ItemRarity.Magic)     //magic items names are mod-sensitive, so force update
                UpdateName();
            Modified = true;
        }
        public void AddImplicit(PoEModData data)
        {
            ModCraft m = new ModCraft(data);
            LiveImplicits.Add(m);
            LiveTags.UnionWith(data.adds_tags);
            UpdateModQuality(m, QualityType);
            Modified = true;
        }
        public void ApplyCatalyst(string tag)
        {
            if (tag == null)
                return;
            if (QualityType != tag)
            {
                QualityType = tag;
                _basequality = 0;       //set this instead of property to avoid triggering extra mod update
            }
            if (Rarity == ItemRarity.Normal)
            {
                BaseQuality += 5;
            }
            else if (Rarity == ItemRarity.Magic)
            {
                BaseQuality += 2;
            }
            else
            {
                BaseQuality += 1;
            }
            Modified = true;
        }
        public void MaximizeMods()
        {
            foreach (ModCraft mod in LiveMods)
            {
                mod.Maximize();
            }
            foreach (ModCraft mod in LiveImplicits)
            {
                mod.Maximize();
            }
            Modified = true;
        }
        private void UpdateModQuality(ModCraft mod, string name)
        {
            PoEModData modtemplate = CraftingDatabase.AllMods[mod.SourceData];
            IList<string> tags;
            if (name != null && ModLogic.CatalystTags.Keys.Contains(name))
                tags = ModLogic.CatalystTags[name];
            else
                tags = new List<string>();
            bool match = false;
            foreach (string s in tags)
            {
                if (modtemplate.type_tags.Contains(s))
                {
                    match = true;
                    break;
                }
            }
            if (modtemplate.type_tags.Contains("jewellery_quality_ignore"))
                match = false;
            if (match)
                mod.Quality = BaseQuality;
            else
                mod.Quality = 0;
        }
        public bool HasValidQualityType()
        {
            string itemclass = CraftingDatabase.AllBaseItems[SourceData].item_class;
            if (CraftingDatabase.ItemClassCatalyst.Contains(itemclass))
                return QualityType != null;
            else if (CraftingDatabase.ItemClassNoQuality.Contains(itemclass))
                return false;
            else
                return QualityType == null;
        }
        public int GetTotalQuality()
        {
            int modbonus = 0;
            foreach (ModCraft m in LiveMods)
            {
                foreach (ModRoll r in m.Stats)
                {
                    if (r.ID == "local_item_quality_+")
                        modbonus += r.Roll;
                }
            }
            return BaseQuality + modbonus;
        }
        public ItemProperties GetLiveProperties()
        {
            if (!Modified)
                return GetLivePropertiesCache;
            IDictionary<string, int> mods = new Dictionary<string, int>();
            IList<string> keys = new List<string>() { "arp", "arf", "evp", "evf", "esp", "esf", "blf", "dp", "mindf", "maxdf", "crp", "asp" }; //all possible property modifiers
            foreach (string s in keys)
            {
                mods.Add(s, 0);
            }
            foreach (ModCraft m in LiveMods)
            {
                ParseProps(m, mods);
            }
            int qual = QualityType == null ? GetTotalQuality() : 0;
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            GetLivePropertiesCache = new ItemProperties()
            {
                armour = (itemtemplate.properties.armour + mods["arf"]) * (100 + mods["arp"] + qual) / 100,
                evasion = (itemtemplate.properties.evasion + mods["evf"]) * (100 + mods["evp"] + qual) / 100,
                energy_shield = (itemtemplate.properties.energy_shield + mods["esf"]) * (100 + mods["esp"] + qual) / 100,
                block = itemtemplate.properties.block + mods["blf"],
                physical_damage_min = (itemtemplate.properties.physical_damage_min + mods["mindf"]) * (100 + mods["dp"] + qual) / 100,
                physical_damage_max = (itemtemplate.properties.physical_damage_max + mods["maxdf"]) * (100 + mods["dp"] + qual) / 100,
                critical_strike_chance = itemtemplate.properties.critical_strike_chance * (100 + mods["crp"]) / 100,
                attack_time = itemtemplate.properties.attack_time * 100 / (100 + mods["asp"])
            };
            Modified = false;
            return GetLivePropertiesCache;
        }
        private void ParseProps(ModCraft m, IDictionary<string, int> mods)
        {
            foreach (ModRoll s in m.Stats)
            {
                switch (s.ID)
                {
                    case "local_physical_damage_reduction_rating_+%":
                        mods["arp"] += s.Roll;
                        break;
                    case "local_evasion_rating_+%":
                        mods["evp"] += s.Roll;
                        break;
                    case "local_energy_shield_+%":
                        mods["esp"] += s.Roll;
                        break;
                    case "local_armour_and_evasion_+%":
                        mods["arp"] += s.Roll;
                        mods["evp"] += s.Roll;
                        break;
                    case "local_armour_and_energy_shield_+%":
                        mods["arp"] += s.Roll;
                        mods["esp"] += s.Roll;
                        break;
                    case "local_evasion_and_energy_shield_+%":
                        mods["evp"] += s.Roll;
                        mods["esp"] += s.Roll;
                        break;
                    case "local_armour_and_evasion_and_energy_shield_+%":
                        mods["arp"] += s.Roll;
                        mods["evp"] += s.Roll;
                        mods["esp"] += s.Roll;
                        break;
                    case "local_base_physical_damage_reduction_rating":
                        mods["arf"] += s.Roll;
                        break;
                    case "local_base_evasion_rating":
                        mods["evf"] += s.Roll;
                        break;
                    case "local_energy_shield":
                        mods["esf"] += s.Roll;
                        break;
                    case "local_additional_block_chance_%":
                        mods["blf"] += s.Roll;
                        break;
                    case "local_physical_damage_+%":
                        mods["dp"] += s.Roll;
                        break;
                    case "local_minimum_added_physical_damage":
                        mods["mindf"] += s.Roll;
                        break;
                    case "local_maximum_added_physical_damage":
                        mods["maxdf"] += s.Roll;
                        break;
                    case "local_critical_strike_chance_+%":
                        mods["crp"] += s.Roll;
                        break;
                    case "local_attack_speed_+%":
                        mods["asp"] += s.Roll;
                        break;
                    default:
                        break;
                }
            }
        }
        private ItemRarity GetMinimumRarity()
        {
            int prefixcount = ModCountByType(ModLogic.Prefix);
            int suffixcount = ModCountByType(ModLogic.Suffix);
            if (prefixcount == 0 && suffixcount == 0)
                return ItemRarity.Normal;
            else if (prefixcount <= 1 && suffixcount <= 1)
                return ItemRarity.Magic;
            else
                return ItemRarity.Rare;
        }
        //removes a mod and updates the item's tags accordingly
        private void RemoveModAt(int n)
        {
            PoEModData modtemplate = CraftingDatabase.AllMods[LiveMods[n].SourceData];
            foreach (string tag in modtemplate.adds_tags)    //for each tag, only remove if no other live mods are applying the tag
            {
                bool shouldremove = true;
                for (int i = 0; i < LiveMods.Count; i++)
                {
                    if (i == n)
                        continue;
                    PoEModData othertemplate = CraftingDatabase.AllMods[LiveMods[i].SourceData];
                    if (othertemplate.adds_tags.Contains(tag))
                    {
                        shouldremove = false;
                        break;
                    }
                }
                if (shouldremove)
                    LiveTags.Remove(tag);
            }
            LiveMods.RemoveAt(n);
        }
        public string GetClipboardString()
        {
            string s = "Rarity: ";
            if (Rarity == ItemRarity.Rare)
                s += "Rare";
            else if (Rarity == ItemRarity.Magic)
                s += "Magic";
            else
                s += "Normal";
            s += "\n" + ItemName;
            s += "\n--------";
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            s += "\n" + itemtemplate.item_class;
            ItemProperties props = GetLiveProperties();
            int q = GetTotalQuality();
            if (q > 0)
            {
                string t;
                switch (QualityType)
                {
                    case "Prismatic Catalyst":
                        t = "Quality (Resistance Modifiers): ";
                        break;
                    case "Fertile Catalyst":
                        t = "Quality (Life and Mana Modifiers): ";
                        break;
                    case "Intrinsic Catalyst":
                        t = "Quality (Attribute Modifiers): ";
                        break;
                    case "Tempering Catalyst":
                        t = "Quality (Defence Modifiers): ";
                        break;
                    case "Abrasive Catalyst":
                        t = "Quality (Attack Modifiers): ";
                        break;
                    case "Imbued Catalyst":
                        t = "Quality (Caster Modifiers): ";
                        break;
                    case "Turbulent Catalyst":
                        t = "Quality (Elemental Damage): ";
                        break;
                    default:
                        t = "Quality: ";
                        break;
                }
                s += "\n" + t + q + "%";
            }
            if (props.block > 0)
                s += "\nChance to Block: " + props.block + "%";
            if (props.armour > 0)
                s += "\nArmour: " + props.armour;
            if (props.evasion > 0)
                s += "\nEvasion: " + props.evasion;
            if (props.energy_shield > 0)
                s += "\nEnergy Shield: " + props.energy_shield;
            if (props.physical_damage_max > 0)
                s += "\nPhysical Damage: " + props.physical_damage_min + "-" + props.physical_damage_max;
            if (props.critical_strike_chance > 0)
                s += "\nCritical Strike Chance: " + ((double)props.critical_strike_chance / 100).ToString("N2") + "%";
            if (props.attack_time > 0)
                s += "\nAttacks per Second: " + ((double)1000 / props.attack_time).ToString("N2");
            s += "\n--------";
            s += "\nItem Level: " + ItemLevel;
            if (LiveImplicits.Count > 0)
            {
                s += "\n--------";
                foreach (ModCraft m in LiveImplicits)
                {
                    s += "\n" + m;
                }
            }
            if (LiveMods.Count > 0)
            {
                s += "\n--------";
                foreach (ModCraft m in LiveMods)
                {
                    s += "\n" + m;
                }
            }
            s += "\n";
            return s;
        }
        public override string ToString()
        {
            return ItemName;
        }
        public void UpdateName()
        {
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            ItemName = itemtemplate.name;
            if (Rarity == ItemRarity.Rare)
            {
                ItemName = GenRareName() + ", " + itemtemplate.name;
            }
            else if (Rarity == ItemRarity.Magic)
            {
                foreach (ModCraft m in LiveMods)
                {
                    PoEModData modtemplate = CraftingDatabase.AllMods[m.SourceData];
                    if (modtemplate.generation_type == ModLogic.Prefix)
                        ItemName = modtemplate.name + " " + ItemName;
                    else if (modtemplate.generation_type == ModLogic.Suffix)
                        ItemName = ItemName + " " + modtemplate.name;
                }
            }
        }
        private string GenRareName()
        {
            PoEBaseItemData itemtemplate = CraftingDatabase.AllBaseItems[SourceData];
            if (namesuffix.Keys.Contains(itemtemplate.item_class))
            {
                string key;
                if (itemtemplate.item_class == "Shield" && itemtemplate.properties.energy_shield > 0 && itemtemplate.properties.armour == 0 && itemtemplate.properties.evasion == 0)
                    key = "Spirit Shield";
                else
                    key = itemtemplate.item_class;
                IList<string> suf = namesuffix[key];
                return nameprefix[RNG.Gen.Next(nameprefix.Count)] + " " + suf[RNG.Gen.Next(suf.Count)];
            }
            else
            {
                return fnames[RNG.Gen.Next(fnames.Count)] + " " + snames[RNG.Gen.Next(snames.Count)];
            }
        }
        private readonly static IList<string> fnames = "Swift,Unceasing,Vengeful,Lone,Cold,Hot,Purple,Brutal,Flying,Driving,Blind,Demon,Enduring,Defiant,Lost,Dying,Falling,Soaring,Twisted,Glass,Bleeding,Broken,Silent,Red,Black,Dark,Sectoid,Fallen,Patient,Burning,Final,Lazy,Morbid,Crimson,Cursed,Frozen,Bloody,Banished,First,Severed,Empty,Spectral,Sacred,Stone,Shattered,Hidden,Rotting,Devil's,Forgotten,Blinding,Fading,Crystal,Secret,Cryptic".Split(',');
        private readonly static IList<string> snames = "Engine,Chant,Heart,Justice,Law,Thunder,Moon,Heat,Fear,Star,Apollo,Prophet,Hero,Hydra,Serpent,Crown,Thorn,Empire,Line,Fall,Summer,Druid,God,Savior,Stallion,Hawk,Vengeance,Calm,Knife,Sword,Dream,Sleep,Stroke,Flame,Spark,Fist,Dirge,Grave,Shroud,Breath,Smoke,Giant,Whisper,Night,Throne,Pipe,Blade,Daze,Pyre,Tears,Mother,Crone,King,Father,Priest,Dawn,Fodder,Hammer,Shield,Hymn,Vanguard,Sentinel,Stranger,Bell,Mist,Fog,Jester,Scepter,Ring,Skull,Paramour,Palace,Mountain,Rain,Gaze,Future,Gift".Split(',');
        private readonly static IList<string> nameprefix = "Agony,Apocalypse,Armageddon,Beast,Behemoth,Blight,Blood,Bramble,Brimstone,Brood,Carrion,Cataclysm,Chimeric,Corpse,Corruption,Damnation,Death,Demon,Dire,Dragon,Dread,Doom,Dusk,Eagle,Empyrean,Fate,Foe,Gale,Ghoul,Gloom,Glyph,Golem,Grim,Hate,Havoc,Honour,Horror,Hypnotic,Kraken,Loath,Maelstrom,Mind,Miracle,Morbid,Oblivion,Onslaught,Pain,Pandemonium,Phoenix,Plague,Rage,Rapture,Rune,Skull,Sol,Soul,Sorrow,Spirit,Storm,Tempest,Torment,Vengeance,Victory,Viper,Vortex,Woe,Wrath".Split(',');
        private readonly static IDictionary<string, IList<string>> namesuffix = new Dictionary<string, IList<string>>()
        {
            { "Spirit Shield" , "Ancient,Anthem,Call,Chant,Charm,Emblem,Guard,Mark,Pith,Sanctuary,Song,Spell,Star,Ward,Weaver,Wish".Split(',') },
            { "Shield" , "Aegis,Badge,Barrier,Bastion,Bulwark,Duty,Emblem,Fend,Guard,Mark,Refuge,Rock,Rook,Sanctuary,Span,Tower,Watch,Wing".Split(',') },
            { "Body Armour" , "Carapace,Cloak,Coat,Curtain,Guardian,Hide,Jack,Keep,Mantle,Pelt,Salvation,Sanctuary,Shell,Shelter,Shroud,Skin,Suit,Veil,Ward,Wrap".Split(',') },
            { "Helmet" , "Brow,Corona,Cowl,Crest,Crown,Dome,Glance,Guardian,Halo,Horn,Keep,Peak,Salvation,Shelter,Star,Veil,Visage,Visor,Ward".Split(',') },
            { "Gloves" , "Caress,Claw,Clutches,Fingers,Fist,Grasp,Grip,Hand,Hold,Knuckle,Mitts,Nails,Palm,Paw,Talons,Touch,Vise".Split(',') },
            { "Boots" , "Dash,Goad,Hoof,League,March,Pace,Road,Slippers,Sole,Span,Spark,Spur,Stride,Track,Trail,Tread,Urge".Split(',') },
            { "Amulet" , "Beads,Braid,Charm,Choker,Clasp,Collar,Idol,Gorget,Heart,Locket,Medallion,Noose,Pendant,Rosary,Scarab,Talisman,Torc".Split(',') },
            { "Ring" , "Band,Circle,Coil,Eye,Finger,Grasp,Grip,Gyre,Hold,Knot,Knuckle,Loop,Nail,Spiral,Turn,Twirl,Whorl".Split(',') },
            { "Belt" , "Bind,Bond,Buckle,Clasp,Cord,Girdle,Harness,Lash,Leash,Lock,Locket,Shackle,Snare,Strap,Tether,Thread,Trap,Twine".Split(',') },
            { "Quiver" , "Arrow,Barb,Bite,Bolt,Brand,Dart,Flight,Hail,Impaler,Nails,Needle,Quill,Rod,Shot,Skewer,Spear,Spike,Spire,Stinger".Split(',') },
            { "One Hand Axe" , "Bane,Beak,Bite,Butcher,Edge,Etcher,Gnash,Hunger,Mangler,Rend,Roar,Sever,Slayer,Song,Spawn,Splitter,Sunder,Thirst".Split(',') },
            { "Two Hand Axe" , "Bane,Beak,Bite,Butcher,Edge,Etcher,Gnash,Hunger,Mangler,Rend,Roar,Sever,Slayer,Song,Spawn,Splitter,Sunder,Thirst".Split(',') },
            { "One Hand Mace" , "Bane,Batter,Blast,Blow,Blunt,Brand,Breaker,Burst,Crack,Crusher,Grinder,Knell,Mangler,Ram,Roar,Ruin,Shatter,Smasher,Star,Thresher,Wreck".Split(',') },
            { "Two Hand Mace" , "Bane,Batter,Blast,Blow,Blunt,Brand,Breaker,Burst,Crack,Crusher,Grinder,Knell,Mangler,Ram,Roar,Ruin,Shatter,Smasher,Star,Thresher,Wreck".Split(',') },
            { "Sceptre" , "Bane,Blow,Breaker,Call,Chant,Crack,Crusher,Cry,Gnarl,Grinder,Knell,Ram,Roar,Smasher,Song,Spell,Star,Weaver".Split(',') },
            { "Staff" , "Bane,Beam,Branch,Call,Chant,Cry,Gnarl,Goad,Mast,Pile,Pillar,Pole,Post,Roar,Song,Spell,Spire,Weaver".Split(',') },
            { "Warstaff" , "Bane,Beam,Branch,Call,Chant,Cry,Gnarl,Goad,Mast,Pile,Pillar,Pole,Post,Roar,Song,Spell,Spire,Weaver".Split(',') },
            { "One Hand Sword" , "Bane,Barb,Beak,Bite,Edge,Fang,Gutter,Hunger,Impaler,Needle,Razor,Saw,Scalpel,Scratch,Sever,Skewer,Slicer,Song,Spike,Spiker,Stinger,Thirst".Split(',') },
            { "Thrusting One Hand Sword" , "Bane,Barb,Beak,Bite,Edge,Fang,Gutter,Hunger,Impaler,Needle,Razor,Saw,Scalpel,Scratch,Sever,Skewer,Slicer,Song,Spike,Spiker,Stinger,Thirst".Split(',') },
            { "Two Hand Sword" , "Bane,Barb,Beak,Bite,Edge,Fang,Gutter,Hunger,Impaler,Needle,Razor,Saw,Scalpel,Scratch,Sever,Skewer,Slicer,Song,Spike,Spiker,Stinger,Thirst".Split(',') },
            { "Dagger" , "Bane,Barb,Bite,Edge,Etcher,Fang,Gutter,Hunger,Impaler,Needle,Razor,Scalpel,Scratch,Sever,Skewer,Slicer,Song,Spike,Stinger,Thirst".Split(',') },
            { "Rune Dagger" , "Bane,Barb,Bite,Edge,Etcher,Fang,Gutter,Hunger,Impaler,Needle,Razor,Scalpel,Scratch,Sever,Skewer,Slicer,Song,Spike,Stinger,Thirst".Split(',') },
            { "Claw" , "Bane,Bite,Edge,Fang,Fist,Gutter,Hunger,Impaler,Needle,Razor,Roar,Scratch,Skewer,Slicer,Song,Spike,Stinger,Talons,Thirst".Split(',') },
            { "Bow" , "Arch,Bane,Barrage,Blast,Branch,Breeze,Fletch,Guide,Horn,Mark,Nock,Rain,Reach,Siege,Song,Stinger,Strike,Thirst,Thunder,Twine,Volley,Wind,Wing".Split(',') },
            { "Wand" , "Bane,Barb,Bite,Branch,Call,Chant,Charm,Cry,Edge,Gnarl,Goad,Needle,Scratch,Song,Spell,Spire,Thirst,Weaver".Split(',') }
        };
    }
}
