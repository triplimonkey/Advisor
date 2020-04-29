﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace HDT.Plugins.Advisor.Services.TempoStorm
{
    public class Include
    {
        public Include()
        {
        }

        public Include(params string[] fields)
        {
            Fields = new List<string>(fields);
        }

        public Include(params IncludeItem[] items)
        {
            Items = new List<IncludeItem>(items);
        }

        [JsonProperty("fields")]
        public List<string> Fields { get; set; }

        [JsonProperty("include")]
        public List<IncludeItem> Items { get; set; }
    }

    public class IncludeItem
    {
        public IncludeItem()
        {
        }

        public IncludeItem(string relation)
        {
            Relation = relation;
        }

        public IncludeItem(string relation, Include scope)
        {
            Relation = relation;
            Scope = scope;
        }

        public IncludeItem(string relation, IncludeItem single)
        {
            Relation = relation;
            Scope = new Include(single);
        }

        [JsonProperty("relation")]
        public string Relation { get; set; }

        [JsonProperty("scope")]
        public Include Scope { get; set; }
    }
}