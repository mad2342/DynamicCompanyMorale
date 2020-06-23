using System.Collections.Generic;

namespace DynamicCompanyMorale
{
    internal class Settings
    {
        public int EventMoraleMultiplier = 4;
        public int EventMoraleDurationBase = 15;
        public int EventMoraleDurationNumerator = 240;

        public bool RespectPilotTags = true;
        public List<string> PositivePilotTags = new List<string>();
        public List<string> NegativePilotTags = new List<string>();
        public int PilotTagImpact = 1;
    }
}