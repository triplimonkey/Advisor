using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Importing;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace HDT.Plugins.Advisor.Services.HsReplay
{
    public class HsReplaySnapshotImporter
    {
        private const string DecksUrl = "https://hsreplay.net/analytics/query/list_decks_by_win_rate_v2/?GameType=RANKED_STANDARD&LeagueRankRange=BRONZE_THROUGH_GOLD&Region=ALL&TimeRange=LAST_30_DAYS";
        private const string ArchetypesUrl = "https://hsreplay.net/api/v1/archetypes/?format=json";

        private const string ArchetypeTag = "Archetype";
        private const string PluginTag = "Advisor";

        private readonly TrackerRepository _tracker;

        // Some decks have no archetype, hsReplay gives them a negative id value
        private static Dictionary<string, HsReplayArchetype> idsWithoutArchetype = new Dictionary<string, HsReplayArchetype>()
        {
            { "-2", new HsReplayArchetype("-4", "DRUID", "Druid Unknown") },
            { "-3", new HsReplayArchetype("-3", "HUNTER", "Hunter  Unknown") },
            { "-4", new HsReplayArchetype("-4", "MAGE", "Mage Unknown") },
            { "-5", new HsReplayArchetype("-5", "PALADIN", "Paladin Unknown") },
            { "-6", new HsReplayArchetype("-6", "PRIEST", "Priest Unknown") },
            { "-7", new HsReplayArchetype("-7", "ROGUE", "Rogue Unknown") },
            { "-8", new HsReplayArchetype("-8", "SHAMAN", "Shaman Unknown") },
            { "-9", new HsReplayArchetype("-9", "WARLOCK", "Warlock Unknown") },
            { "-10", new HsReplayArchetype("-10", "WARRIOR", "Warrior Unknown") },
            { "-14", new HsReplayArchetype("-14", "DEMONHUNTER", "Demon Hunter Unknown") },
        };

        private static Dictionary<string, string> hsReplayClassToDeckTrackerClass = new Dictionary<string, string>()
        {
            { "DRUID", "Druid" },
            { "HUNTER", "Hunter" },
            { "MAGE", "Mage" },
            { "PALADIN", "Paladin" },
            { "PRIEST", "Priest" },
            { "ROGUE", "Rogue" },
            { "SHAMAN", "Shaman" },
            { "WARLOCK", "Warlock" },
            { "WARRIOR", "Warrior" },
            { "DEMONHUNTER", "DemonHunter" },
        };

        // default value in case no archetype is found
        private static HsReplayArchetype unknownArchetype = new HsReplayArchetype("", "UNKNOWN", "Unknown");

        public HsReplaySnapshotImporter(TrackerRepository tracker)
        {
            _tracker = tracker;
        }

        private static void Notify(string message, int timeout = 0)
        {
            Log.Info(message);
            Advisor.Notify("Importing decks", message, timeout);
        }

        public async Task<int> ImportDecks(bool autoArchiveImportedDecks, bool deletePreviousImportedDecks, bool shortenDeckTitle, IProgress<Tuple<int, int>> uiProgress, string hsReplayJson = null, string hsReplayArchetypesJson = null)
        {
            if (deletePreviousImportedDecks)
            {
                DeleteDecks();
            }

            List<Deck> decks = await GetClassDecks(hsReplayJson, hsReplayArchetypesJson);

            return await AddDecksToTracker(decks, autoArchiveImportedDecks, shortenDeckTitle);
        }

        private async Task<List<Deck>> GetClassDecks(string hsReplayJson = null, string hsArchetypesJson = null)
        {
            Notify("Fetching decks from HsReplay");
            Log.Info($"Using url {DecksUrl}");

            hsReplayJson = hsReplayJson != null ? hsReplayJson : await ImportingHelper.JsonRequest(DecksUrl);
            hsArchetypesJson = hsArchetypesJson != null ? hsArchetypesJson : await ImportingHelper.JsonRequest(ArchetypesUrl);

            List<JToken> decksJson = JObject.Parse(hsReplayJson).SelectTokens(@"$.series.data.*").Values().ToList();

            Notify($"Found {decksJson.Count}");

            List<HsReplayArchetype> archetypes = JsonConvert.DeserializeObject<List<HsReplayArchetype>>(hsArchetypesJson);
            Dictionary<string, HsReplayArchetype> archetypesById = archetypes.ToDictionary(archetype => archetype.Id);
            Dictionary<string, HsReplayArchetype> allArchetypesById = archetypesById.Concat(idsWithoutArchetype)
                .ToLookup(x => x.Key, x => x.Value)
                .ToDictionary(x => x.Key, g => g.First());

            return decksJson.Select(deckJson => GetDeck(deckJson, allArchetypesById)).ToList();
        }

        private Deck GetDeck(JToken deckJson, Dictionary<string, HsReplayArchetype> allArchetypesById) {
            HsReplayDeck hsReplayDeck = deckJson.ToObject<HsReplayDeck>();
            Deck deck = new Deck();

            HsReplayArchetype archetype = allArchetypesById.TryGetValue(hsReplayDeck.ArchetypeId, out HsReplayArchetype value) ? value : unknownArchetype;
            deck.Name = archetype.Name;
            deck.Class = hsReplayClassToDeckTrackerClass[archetype.Class];
            deck.Cards = GetCardsFromHsReplayDeckList(hsReplayDeck.DeckList);
            deck.LastEdited = DateTime.Now;

            return deck;
        }

        private ObservableCollection<Card> GetCardsFromHsReplayDeckList(string deckListString)
        {
            JArray deckArray = JArray.Parse(deckListString); // List<Tuple<String, String>>
            IEnumerable<Card> cards = deckArray.Select(cardDbfId_Count => {
                Card card = Database.GetCardFromDbfId(cardDbfId_Count[0].Value<int>());
                card.Count = cardDbfId_Count[1].Value<int>();
                return card;
            });

            return new ObservableCollection<Card>(cards);
        }

        private async Task<int> AddDecksToTracker(List<Deck> decks, bool autoArchiveImportedDecks, bool shortenDeckTitle)
        {
            var deckCount = await Task.Run(() => SaveDecks(decks, autoArchiveImportedDecks, shortenDeckTitle));

            if (deckCount != decks.Count)
            {
                Notify($"Only {deckCount} of {decks.Count} archetype could be imported. Connection problems?");
            }

            return deckCount;
        }

        private int SaveDecks(IEnumerable<Deck> decks, bool autoArchiveImportedDecks, bool shortenDeckTitle)
        {
            var deckCount = 0;

            foreach (var deck in decks)
            {
                if (deck == null)
                {
                    throw new ImportException("At least one deck couldn't be imported. Connection problems?");
                }

                Log.Info($"Importing deck ({deck.Name})");

                // Optionally remove player class from deck name
                // E.g. 'Control Warrior' => 'Control'
                var deckName = deck.Name;
                if (shortenDeckTitle)
                {
                    deckName = deckName.Replace(deck.Class, "").Trim();
                    deckName = deckName.Replace("Demon Hunter", "");
                    deckName = deckName.Replace("  ", " ");
                }

                _tracker.AddDeck(deckName, deck, autoArchiveImportedDecks, ArchetypeTag, PluginTag);
                deckCount++;
            }

            DeckList.Save();
            return deckCount;
        }

        public int DeleteDecks()
        {
            Log.Info("Deleting all archetype decks");
            return _tracker.DeleteAllDecksWithTag(PluginTag);
        }

        class HsReplayDeck
        {
            [JsonProperty("deck_id")]
            public string DeckId { get; set; }

            [JsonProperty("archetype_id")]
            public string ArchetypeId { get; set; }

            [JsonProperty("deck_list")]
            public string DeckList { get; set; }

            public string Name { get; set; }
            public string ClassId { get; set; }
        }

        class HsReplayArchetype
        {
            public HsReplayArchetype(string Id, string Class, string Name)
            {
                this.Id = Id;
                this.Class = Class;
                this.Name = Name;
            }

            [JsonProperty("id")]
            public string Id { get; set; }

            //[JsonProperty("player_class")]
            //public string ClassId { get; set; }

            [JsonProperty("player_class_name")]
            public string Class { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }
        }
    }
}