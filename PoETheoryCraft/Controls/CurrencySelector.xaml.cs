﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using PoETheoryCraft.DataClasses;

namespace PoETheoryCraft.Controls
{
    
    /// <summary>
    /// Interaction logic for CurrencySelector.xaml
    /// </summary>
    public partial class CurrencySelector : UserControl
    {
        public class CurrencyEventArgs : EventArgs
        {
            public PoECurrencyData Currency { get; set; }
            public PoEEssenceData Essence { get; set; }
            public IList<PoEFossilData> Fossils { get; set; }
        }

        public event EventHandler<CurrencyEventArgs> CurrencySelectionChanged;
        public CurrencySelector()
        {
            InitializeComponent();
        }
        public void LoadEssences(ICollection<PoEEssenceData> essences)
        {
            CollectionViewSource.GetDefaultView(essences).Filter = AllowedEssences;
            EssenceView.ItemsSource = essences;
            if (EssenceView.Items.Count > 0)
                EssenceView.SelectedIndex = 0;
        }
        public void LoadFossils(ICollection<PoEFossilData> fossils)
        {
            CollectionViewSource.GetDefaultView(fossils).Filter = AllowedFossils;
            FossilView.ItemsSource = fossils;
        }
        public void LoadCurrencies(ICollection<PoECurrencyData> currencies)
        {
            List<PoECurrencyData> clist = currencies.ToList<PoECurrencyData>();
            clist.Sort((a, b) => CraftingDatabase.CurrencyIndex.IndexOf(a.name).CompareTo(CraftingDatabase.CurrencyIndex.IndexOf(b.name)));
            CollectionViewSource.GetDefaultView(clist).Filter = AllowedCurrencies;
            BasicView.ItemsSource = clist;
            if (BasicView.Items.Count > 0)
                BasicView.SelectedIndex = 0;
        }
        public void CurrencyTabs_SelectionChanged(object sender, RoutedEventArgs e)
        {
            switch (CurrencyTabs.SelectedIndex)
            {
                case 0:
                    CurrencySelectionChanged?.Invoke(this, new CurrencyEventArgs() { Currency = BasicView.SelectedItem as PoECurrencyData });
                    break;
                case 1:
                    SolidColorBrush b = FossilView.SelectedItems.Count > 4 ? Brushes.Red : Brushes.Black;
                    FossilLabel1.Foreground = b;
                    FossilLabel2.Foreground = b;
                    CurrencySelectionChanged?.Invoke(this, new CurrencyEventArgs() { Fossils = ((System.Collections.IList)FossilView.SelectedItems).Cast<PoEFossilData>().ToList() });
                    break;
                case 2:
                    CurrencySelectionChanged?.Invoke(this, new CurrencyEventArgs() { Essence = EssenceView.SelectedItem as PoEEssenceData });
                    break;
                default:
                    return;
            }
        }
        public object GetSelected()
        {
            switch (CurrencyTabs.SelectedIndex)
            {
                case 0:
                    return BasicView.SelectedItem;
                case 1:
                    return FossilView.SelectedItems;
                case 2:
                    return EssenceView.SelectedItem;
                default:
                    return null;
            }
        }
        private bool AllowedEssences(object o)
        {
            PoEEssenceData e = o as PoEEssenceData;
            return e.level > 0;
        }
        private bool AllowedFossils(object o)
        {
            PoEFossilData f = o as PoEFossilData;
            return (!f.changes_quality && !f.enchants && !f.mirrors && !f.rolls_lucky && !f.rolls_white_sockets && f.sell_price_mods.Count == 0);
        }
        private bool AllowedCurrencies(object o)
        {
            PoECurrencyData c = o as PoECurrencyData;
            if (c.name == "Vaal Orb" || c.name == "Orb of Chance" || c.name == "Glassblower's Bauble")
                return false;
            return true;
        }
    }
}
