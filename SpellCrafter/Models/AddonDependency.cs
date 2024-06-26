﻿using SQLite;
using SQLiteNetExtensions.Attributes;

namespace SpellCrafter.Models
{
    public class LocalAddonDependency
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [ForeignKey(typeof(LocalAddon))]
        public int LocalAddonId { get; set; }

        [ForeignKey(typeof(CommonAddon))]
        public int DependentCommonAddonId { get; set; }
    }
}