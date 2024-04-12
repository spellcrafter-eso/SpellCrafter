﻿using SpellCrafter.Models.DbClasses;

namespace SpellCrafter.Messages
{
    public class AddonUpdatedMessage
    {
        public Addon? UpdatedAddon { get; private set; }

        public AddonUpdatedMessage(Addon? updatedAddon)
        {
            UpdatedAddon = updatedAddon;
        }
    }
}