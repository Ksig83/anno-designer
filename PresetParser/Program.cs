﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using AnnoDesigner;
using AnnoDesigner.Presets;

namespace PresetParser
{
    public class Program
    {
        private static readonly string[] Languages = new[] { "ger", "eng" };

        private static string GetIconFilename(XmlNode iconNode)
        {
            return string.Format("icon_{0}_{1}.png", iconNode["IconFileID"].InnerText, iconNode["IconIndex"] != null ? iconNode["IconIndex"].InnerText : "0");
        }

        public static void Main(string[] args)
        {
            // prepare localizations
            var localizations = GetLocalizations();

            // prepare icon mapping
            var iconsDocument = new XmlDocument();
            iconsDocument.Load("data/config/icons.xml");
            var iconNodes = iconsDocument.SelectNodes("/Icons/i").Cast<XmlNode>();

            // write icon name mapping
            Console.WriteLine("Writing icon name mapping to icons.json");
            WriteIconNameMapping(iconNodes, localizations);

            // parse buildings
            var buildings = new List<BuildingInfo>();

            // find buildings in assets.xml
            Console.WriteLine();
            Console.WriteLine("Parsing assets.xml:");
            ParseAssetsFile("data/config/assets.xml", "/AssetList/Groups/Group/Groups/Group", buildings, iconNodes, localizations);
            
            // find buildings in addon_01_assets.xml
            Console.WriteLine();
            Console.WriteLine("Parsing addon_01_assets.xml:");
            ParseAssetsFile("data/config/addon_01_assets.xml", "/Group/Groups/Group", buildings, iconNodes, localizations);

            // serialize presets to json file
            var presets = new BuildingPresets { Version = "0.5", Buildings = buildings };
            Console.WriteLine("Writing buildings to presets.json");
            DataIO.SaveToFile(presets, "presets.json");
            
            // wait for keypress before exiting
            Console.WriteLine();
            Console.WriteLine("DONE - press enter to exit");
            Console.ReadLine();
        }

        private static void ParseAssetsFile(string filename, string xPathToBuildingsNode, List<BuildingInfo> buildings,
            IEnumerable<XmlNode> iconNodes, Dictionary<string, SerializableDictionary<string>> localizations)
        {
            var assetsDocument = new XmlDocument();
            assetsDocument.Load(filename);
            var buildingNodes = assetsDocument.SelectNodes(xPathToBuildingsNode)
                .Cast<XmlNode>().Single(_ => _["Name"].InnerText == "Buildings");
            foreach (var buildingNode in buildingNodes.SelectNodes("Groups/Group/Groups/Group/Assets/Asset").Cast<XmlNode>())
            {
                ParseBuilding(buildings, buildingNode, iconNodes, localizations);
            }
        }

        private static void ParseBuilding(List<BuildingInfo> buildings, XmlNode buildingNode, IEnumerable<XmlNode> iconNodes, Dictionary<string, SerializableDictionary<string>> localizations)
        {
            var values = buildingNode["Values"];
            // skip invalid elements
            if (buildingNode["Template"] == null)
            {
                return;
            }
            // parse stuff
            var b = new BuildingInfo
            {
                Faction = buildingNode.ParentNode.ParentNode.ParentNode.ParentNode["Name"].InnerText,
                Group = buildingNode.ParentNode.ParentNode["Name"].InnerText,
                Template = buildingNode["Template"].InnerText,
                Identifier = values["Standard"]["Name"].InnerText
            };
            // print progress
            Console.WriteLine(b.Identifier);
            // parse building blocker
            if (!RetrieveBuildingBlocker(b, values["Object"]["Variations"].FirstChild["Filename"].InnerText))
            {
                return;
            }
            // find icon node based on guid match
            var buildingGuid = values["Standard"]["GUID"].InnerText;
            var icon = iconNodes.SingleOrDefault(_ => _["GUID"].InnerText == buildingGuid);
            if (icon != null)
            {
                b.IconFileName = GetIconFilename(icon["Icons"].FirstChild);
            }
            // read influence radius if existing
            try
            {
                b.InfluenceRadius = Convert.ToInt32(values["Influence"]["InfluenceRadius"].InnerText);
            }
            catch (NullReferenceException ex) { }
            // find localization
            if (localizations.ContainsKey(buildingGuid))
            {
                b.Localization = localizations[buildingGuid];
            }
            // add building to the list
            buildings.Add(b);
        }

        private static bool RetrieveBuildingBlocker(BuildingInfo building, string variationFilename)
        {
            var ifoDocument = new XmlDocument();
            ifoDocument.Load(Path.Combine("data/ifo/", string.Format("{0}.ifo", Path.GetFileNameWithoutExtension(variationFilename))));
            try
            {
                var node = ifoDocument.FirstChild["BuildBlocker"].FirstChild;
                building.BuildBlocker = new SerializableDictionary<int>();
                building.BuildBlocker["x"] = Math.Abs(Convert.ToInt32(node["x"].InnerText) / 2048);
                building.BuildBlocker["z"] = Math.Abs(Convert.ToInt32(node["z"].InnerText) / 2048);
            }
            catch (NullReferenceException)
            {
                Console.WriteLine("-BuildBlocker not found, skipping");
                return false;
            }
            return true;
        }

        private class GuidRef
        {
            public string Language;
            public string Guid;
            public string GuidReference;
        }

        private static Dictionary<string, SerializableDictionary<string>> GetLocalizations()
        {
            string[] files = { "icons.txt", "guids.txt", "addon/texts.txt" };
            var localizations = new Dictionary<string, SerializableDictionary<string>>();
            var references = new List<GuidRef>();
            foreach (var language in Languages)
            {
                var basePath = Path.Combine("data/languages", language, "txt");
                foreach (var path in files.Select(_ => Path.Combine(basePath, _)))
                {
                    if (!File.Exists(path)) continue;
                    var reader = new StreamReader(path);
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        // skip commentary and empty lines
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        {
                            continue;
                        }
                        // split lines and skip invalid results
                        var separator = line.IndexOf('=');
                        if (separator == -1)
                        {
                            continue;
                        }
                        var guid = line.Substring(0, separator);
                        var translation = line.Substring(separator+1);
                        // add new entry if needed
                        if (!localizations.ContainsKey(guid))
                        {
                            localizations.Add(guid, new SerializableDictionary<string>());
                        }
                        // add localization string
                        localizations[guid][language] = translation;
                        // remember entry if guid it is a reference to another guid
                        if (translation.StartsWith("[GUIDNAME"))
                        {
                            references.Add(new GuidRef
                            {
                                Language = language,
                                Guid = guid,
                                GuidReference = translation.Substring(10, translation.Length-11)
                            });
                        }
                    }
                }
            }
            // copy over references
            foreach (var reference in references)
            {
                if (localizations.ContainsKey(reference.GuidReference))
                {
                    localizations[reference.Guid][reference.Language] =
                        localizations[reference.GuidReference][reference.Language];
                }
                else
                {
                    localizations.Remove(reference.Guid);
                }
            }
            return localizations;
        }

        private static void WriteIconNameMapping(IEnumerable<XmlNode> iconNodes, Dictionary<string, SerializableDictionary<string>> localizations)
        {
            var mapping = new List<IconNameMap>();
            foreach (var iconNode in iconNodes)
            {
                var guid = iconNode["GUID"].InnerText;
                var iconFilename = GetIconFilename(iconNode["Icons"].FirstChild);
                if (!localizations.ContainsKey(guid) || mapping.Exists(_ => _.IconFilename == iconFilename))
                {
                    continue;
                }
                mapping.Add(new IconNameMap
                {
                    IconFilename = iconFilename,
                    Localizations = localizations[guid]
                });
            }
            DataIO.SaveToFile(mapping, "icons.json");
        }
    }
}
