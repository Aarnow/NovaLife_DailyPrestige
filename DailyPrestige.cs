using DailyPrestige.Classes;
using DailyPrestige.Entities;
using DailyPrestige.Points;
using Life;
using Life.Network;
using Life.UI;
using ModKit.Helper;
using ModKit.Interfaces;
using ModKit.Utils;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using _menu = AAMenu.Menu;
using mk = ModKit.Helper.TextFormattingHelper;

namespace DailyPrestige
{
    public class DailyPrestige : ModKit.ModKit
    {
        public static string ConfigDirectoryPath;
        public static string ConfigDailyPrestigePath;
        public static DailyPrestigeConfig _dailyPrestigeConfig;
        private readonly Events events;
        System.Random random = new System.Random();
        public DailyPrestige(IGameAPI api) : base(api)
        {
            PluginInformations = new PluginInformations(AssemblyHelper.GetName(), "1.0.0", "Aarnow");
            events = new Events(api);
        }

        public override void OnPluginInit()
        {
            base.OnPluginInit();
            events.Init(Nova.server);
            InitConfig();
            _dailyPrestigeConfig = LoadConfigFile(ConfigDailyPrestigePath);

            Orm.RegisterTable<DailyPrestige_Task>();
            Orm.RegisterTable<DailyPrestige_Reward>();
            Orm.RegisterTable<DailyPrestige_Player>();

            Orm.RegisterTable<Deposit>();
            PointHelper.AddPattern("Deposit", new Deposit(false));
            AAMenu.AAMenu.menu.AddBuilder(PluginInformations, "Deposit", new Deposit(false), this);

            InsertMenu();
            Nova.man.StartCoroutine(Cycle());

            ModKit.Internal.Logger.LogSuccess($"{PluginInformations.SourceName} v{PluginInformations.Version}", "initialisé");
        }

        #region Config
        private void InitConfig()
        {
            try
            {
                ConfigDirectoryPath = DirectoryPath + "/DailyPrestige";
                ConfigDailyPrestigePath = Path.Combine(ConfigDirectoryPath, "dailyPrestigeConfig.json");

                if (!Directory.Exists(ConfigDirectoryPath)) Directory.CreateDirectory(ConfigDirectoryPath);
                if (!File.Exists(ConfigDailyPrestigePath)) InitDailyPrestigeConfig();
            }
            catch (IOException ex)
            {
                ModKit.Internal.Logger.LogError("InitDirectory", ex.Message);
            }
        }

        private void InitDailyPrestigeConfig()
        {
            DailyPrestigeConfig dailyPrestigeConfig = new DailyPrestigeConfig();
            string json = JsonConvert.SerializeObject(dailyPrestigeConfig);
            File.WriteAllText(ConfigDailyPrestigePath, json);
        }

        private DailyPrestigeConfig LoadConfigFile(string path)
        {
            if (File.Exists(path))
            {
                string jsonContent = File.ReadAllText(path);
                DailyPrestigeConfig dailyPrestigeConfig = JsonConvert.DeserializeObject<DailyPrestigeConfig>(jsonContent);

                return dailyPrestigeConfig;
            }
            else return null;
        }
        #endregion

        #region COROUTINE
        public IEnumerator Cycle()
        {
            while (true)
            {
                TimeSpan timeUntilMidnight = GetTimeUntilMidnight();
                Console.WriteLine($"Temps restant avant le prochain déclenchement : {timeUntilMidnight.Hours}h {timeUntilMidnight.Minutes}m {timeUntilMidnight.Seconds}s");

                var query = GetDailyTask();
                yield return new WaitUntil(() => query.IsCompleted);
                bool result = query.Result;

                if (result) yield return new WaitForSeconds((float)timeUntilMidnight.TotalSeconds); // Attendre jusqu'à minuit
                else yield return new WaitForSeconds(2 * 60); // réessayer dans 2 minutes
            }
        }

        private TimeSpan GetTimeUntilMidnight()
        {
            DateTime now = DateTime.Now;
            DateTime midnight = now.Date.AddDays(1);
            return midnight - now;
        }

        private async Task<bool> GetDailyTask()
        {
            List<DailyPrestige_Task> tasks = await DailyPrestige_Task.QueryAll();
            if(tasks != null && tasks.Count > 0)
            {
                DailyPrestige_Task dailyTask = tasks.Where(t => t.Date == DateUtils.GetNumericalDateOfTheDay()).FirstOrDefault();
                if (dailyTask == null || dailyTask == default)
                {
                    dailyTask = tasks[random.Next(tasks.Count)];
                    dailyTask.Date = DateUtils.GetNumericalDateOfTheDay();
                    return await dailyTask.Save();
                }
            }
            
            return false;
        }
        #endregion

        public void InsertMenu()
        {
            _menu.AddAdminTabLine(PluginInformations, 5, "DailyPrestige", (ui) =>
            {
                Player player = PanelHelper.ReturnPlayerFromPanel(ui);
                DailyPrestigePanel(player);
            });
        }

        public void DailyPrestigePanel(Player player)
        {
            //Déclaration
            Panel panel = PanelHelper.Create("DailyPrestige", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanel(player));

            //Corps
            panel.AddTabLine("Liste des tâches", _ => DailyPrestigePanelTask(player));
            panel.AddTabLine("Liste des récompenses", _ => DailyPrestigePanelReward(player));
            panel.AddTabLine("Liste des contributeurs", _ => DailyPrestigePanelPlayer(player));

            panel.NextButton("Sélectionner", () => panel.SelectTab());
            panel.AddButton("Retour", _ => AAMenu.AAMenu.menu.AdminPanel(player, AAMenu.AAMenu.menu.AdminTabLines));
            panel.CloseButton();

            //Affichage
            panel.Display();
        }

        #region TASK
        public async void DailyPrestigePanelTask(Player player)
        {
            //Query
            List<DailyPrestige_Task> tasks = await DailyPrestige_Task.QueryAll();

            //Déclaration
            Panel panel = PanelHelper.Create("DailyPrestige - Tâches", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelTask(player));

            //Corps
            if(tasks.Count > 0)
            {
                foreach (var task in tasks)
                {
                    var currentItem = ItemUtils.GetItemById(task.ItemId);
                    panel.AddTabLine($"{(currentItem != null ? $"{mk.Size($"Apporter {mk.Color($"{task.Quantity} {currentItem.itemName}",mk.Colors.Orange)}<br> pour soutenir \"{task.Name}\"",14)}": $"{mk.Color("tâche incomplète",mk.Colors.Error)}")}",
                        $"{(currentItem != null ? $"{mk.Size($"Objectif {task.ObjectiveCounter} contributions", 14)}" : "")}", 
                        ItemUtils.GetIconIdByItemId(task.ItemId), _ =>
                    {
                        DailyPrestigePanelTaskDetails(player, task);
                    });
                }
            } else panel.AddTabLine("Aucune tâche", _ => { });
            

            panel.NextButton("Ajouter", () => DailyPrestigePanelTaskDetails(player));
            if (tasks.Count > 0) panel.NextButton("Modifier", () => panel.SelectTab());
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void DailyPrestigePanelTaskDetails(Player player, DailyPrestige_Task task = null)
        {
            if(task == null)
            {
                task = new DailyPrestige_Task();
                task.Name = "Nouvelle tâche";
            }
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Gestion d'une tâche", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelTaskDetails(player, task));

            //Corps
            panel.AddTabLine($"{mk.Color("Association:", mk.Colors.Info)} {task.Name}", _ => TaskSetName(player, task));
            panel.AddTabLine($"{mk.Color("Objets à apporter:", mk.Colors.Info)} {(task.ItemId != default ? $"{task.Quantity} {ItemUtils.GetItemById(task.ItemId).itemName}" : "à définir")}","", ItemUtils.GetIconIdByItemId(task.ItemId), _ => TaskSetItem(player, task));
            panel.AddTabLine($"{mk.Color("Objectif commun:", mk.Colors.Info)} {(task.ObjectiveCounter != default ? $"{task.ObjectiveCounter} accomplissements" : "à définir")}", _ => TaskSetObjectiveCounter(player, task));


            panel.NextButton("Sélectionner", () => panel.SelectTab());
            if (task.Id != default && task.Date != DateUtils.GetNumericalDateOfTheDay())
            {
                panel.PreviousButtonWithAction("Supprimer", async () =>
                {
                    if (await task.Delete())
                    {
                        player.Notify("DailyPrestige", "Tâche supprimée", NotificationManager.Type.Success);
                        return true;
                    }
                    else
                    {
                        player.Notify("DailyPrestige", "Nous n'avons pas pu supprimer cette tâche", NotificationManager.Type.Error);
                        return false;
                    }
                });
            }
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }

        #region SETTERS
        public void TaskSetName(Player player, DailyPrestige_Task task)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Nom de l'association", UIPanel.PanelType.Input, player, () => TaskSetName(player, task));

            //Corps
            panel.TextLines.Add("Définir le nom de l'association à soutenir");
            panel.inputPlaceholder = "3 caractères minimum";

            //Boutons
            panel.PreviousButtonWithAction("Sauvegarder", async () =>
            {
                if (panel.inputText.Length >= 3)
                {
                    task.Name = panel.inputText;

                    if (await task.Save())
                    {
                        player.Notify("DailyStorage", "tâche enregistrée", NotificationManager.Type.Success);
                        return true;
                    }
                    else
                    {
                        player.Notify("DailyStorage", "Nous n'avons pas pu enregistrer votre tâche", NotificationManager.Type.Error);
                        return false;
                    }
                }
                else
                {
                    player.Notify("DailyStorage", "Format incorrect", NotificationManager.Type.Warning);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void TaskSetItem(Player player, DailyPrestige_Task task)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Objets à apporter", UIPanel.PanelType.Input, player, () => TaskSetItem(player, task));

            //Corps
            panel.TextLines.Add("Définir l'ID de l'objet et le nombre qu'il faut apporter");
            panel.inputPlaceholder = "[ID] [QUANTITÉ]";

            //Boutons
            panel.PreviousButtonWithAction("Sauvegarder", async () =>
            {
                string pattern = @"^(\d+)\s(\d+)$";
                Match match = Regex.Match(panel.inputText, pattern);

                if (match.Success)
                {
                    var currentItem = ItemUtils.GetItemById(int.Parse(match.Groups[1].Value));

                    if(currentItem != null)
                    {
                        var quantity = int.Parse(match.Groups[2].Value);

                        if(quantity > 0)
                        {
                            task.ItemId = currentItem.id;
                            task.Quantity = quantity;

                            if (await task.Save())
                            {
                                player.Notify("DailyStorage", "tâche enregistrée", NotificationManager.Type.Success);
                                return true;
                            }
                            else
                            {
                                player.Notify("DailyStorage", "Nous n'avons pas pu enregistrer votre tâche", NotificationManager.Type.Error);
                                return false;
                            }
                        }
                        else
                        {
                            player.Notify("DailyStorage", "La quantité à fournir doit être supérieure", NotificationManager.Type.Warning);
                            return false;
                        }
                    }
                    else
                    {
                        player.Notify("DailyStorage", "L'objet renseigné n'existe pas", NotificationManager.Type.Warning);
                        return false;
                    }
                }
                else
                {
                    player.Notify("DailyStorage", "Format incorrect", NotificationManager.Type.Warning);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void TaskSetObjectiveCounter(Player player, DailyPrestige_Task task)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Objectif d'accomplissement", UIPanel.PanelType.Input, player, () => TaskSetObjectiveCounter(player, task));

            //Corps
            panel.TextLines.Add("Combien de fois cette tâche doit-elle être résolue pour accorder un bonus à la ville ?");
            panel.inputPlaceholder = "exemple: 8";

            //Boutons
            panel.PreviousButtonWithAction("Sauvegarder", async () =>
            {
                if (int.TryParse(panel.inputText, out int objectiveCount))
                {
                    if(objectiveCount > 0)
                    {
                        task.ObjectiveCounter = objectiveCount;

                        if (await task.Save())
                        {
                            player.Notify("DailyStorage", "tâche enregistrée", NotificationManager.Type.Success);
                            return true;
                        }
                        else
                        {
                            player.Notify("DailyStorage", "Nous n'avons pas pu enregistrer votre tâche", NotificationManager.Type.Error);
                            return false;
                        }
                    }
                    else
                    {
                        player.Notify("DailyStorage", "Renseigner une valeur positive", NotificationManager.Type.Warning);
                        return false;
                    }
                }
                else
                {
                    player.Notify("DailyStorage", "Format incorrect", NotificationManager.Type.Warning);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        #endregion
        #endregion

        #region REWARDS
        public async void DailyPrestigePanelReward(Player player)
        {
            //Query
            List<DailyPrestige_Reward> rewards = await DailyPrestige_Reward.QueryAll();

            //Déclaration
            Panel panel = PanelHelper.Create("DailyPrestige - Récompenses", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelReward(player));

            //Corps
            if (rewards.Count > 0)
            {
                foreach (var reward in rewards)
                {
                    var currentItem = ItemUtils.GetItemById(reward.ItemId);
                    panel.AddTabLine($"{currentItem.itemName} x {reward.ItemQuantity}", $"Prestige requis: {reward.PrestigeRequired}", ItemUtils.GetIconIdByItemId(currentItem.id), _ =>
                    {
                        DailyPrestigePanelRewardDetails(player, reward);
                    });
                }
            }
            else panel.AddTabLine("Aucune récompense", _ => { });


            panel.NextButton("Ajouter", () => DailyPrestigePanelRewardDetails(player));
            if (rewards.Count > 0) panel.NextButton("Modifier", () => panel.SelectTab());
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void DailyPrestigePanelRewardDetails(Player player, DailyPrestige_Reward reward = null)
        {
            if (reward == null) reward = new DailyPrestige_Reward();
            
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Gestion d'une récompense", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelRewardDetails(player, reward));

            //Corps
            panel.AddTabLine($"{mk.Color("Récompense:", mk.Colors.Info)} {(reward.ItemId != default ? $"{ItemUtils.GetItemById(reward.ItemId).itemName} x {reward.ItemQuantity}" : "à définir")}", _ => RewardSetItem(player, reward));
            panel.AddTabLine($"{mk.Color("Prestige requis:", mk.Colors.Info)} {reward.PrestigeRequired}", _ => RewardSetPrestigeRequired(player, reward));


            panel.NextButton("Sélectionner", () => panel.SelectTab());
            if (reward.Id != default)
            {
                panel.PreviousButtonWithAction("Supprimer", async () =>
                {
                    if (await reward.Delete())
                    {
                        player.Notify("DailyPrestige", "Récompense supprimée", NotificationManager.Type.Success);
                        return true;
                    }
                    else
                    {
                        player.Notify("DailyPrestige", "Nous n'avons pas pu supprimer cette récompense", NotificationManager.Type.Error);
                        return false;
                    }
                });
            }
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }

        #region SETTERS
        public void RewardSetItem(Player player, DailyPrestige_Reward reward)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Récompense", UIPanel.PanelType.Input, player, () => RewardSetItem(player, reward));

            //Corps
            panel.TextLines.Add("Définir l'ID de l'objet et le nombre à offrir en récompense");
            panel.inputPlaceholder = "[ID] [QUANTITÉ]";

            //Boutons
            panel.PreviousButtonWithAction("Sauvegarder", async () =>
            {
                string pattern = @"^(\d+)\s(\d+)$";
                Match match = Regex.Match(panel.inputText, pattern);

                if (match.Success)
                {
                    var currentItem = ItemUtils.GetItemById(int.Parse(match.Groups[1].Value));

                    if (currentItem != null)
                    {
                        var quantity = int.Parse(match.Groups[2].Value);

                        if (quantity > 0)
                        {
                            reward.ItemId = currentItem.id;
                            reward.ItemQuantity = quantity;

                            if (await reward.Save())
                            {
                                player.Notify("DailyStorage", "récompense enregistrée", NotificationManager.Type.Success);
                                return true;
                            }
                            else
                            {
                                player.Notify("DailyStorage", "Nous n'avons pas pu enregistrer votre récompense", NotificationManager.Type.Error);
                                return false;
                            }
                        }
                        else
                        {
                            player.Notify("DailyStorage", "La quantité à fournir doit être supérieure", NotificationManager.Type.Warning);
                            return false;
                        }
                    }
                    else
                    {
                        player.Notify("DailyStorage", "L'objet renseigné n'existe pas", NotificationManager.Type.Warning);
                        return false;
                    }
                }
                else
                {
                    player.Notify("DailyStorage", "Format incorrect", NotificationManager.Type.Warning);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void RewardSetPrestigeRequired(Player player, DailyPrestige_Reward reward)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Prestige requis", UIPanel.PanelType.Input, player, () => RewardSetPrestigeRequired(player, reward));

            //Corps
            panel.TextLines.Add("Combien de prestige requis pour récupérer la récompense");
            panel.inputPlaceholder = "exemple: 10";

            //Boutons
            panel.PreviousButtonWithAction("Sauvegarder", async () =>
            {
                if (int.TryParse(panel.inputText, out int prestigeRequired))
                {
                    if (prestigeRequired > 0)
                    {
                        reward.PrestigeRequired = prestigeRequired;

                        if (await reward.Save())
                        {
                            player.Notify("DailyStorage", "récompense enregistrée", NotificationManager.Type.Success);
                            return true;
                        }
                        else
                        {
                            player.Notify("DailyStorage", "Nous n'avons pas pu enregistrer votre récompense", NotificationManager.Type.Error);
                            return false;
                        }
                    }
                    else
                    {
                        player.Notify("DailyStorage", "Renseigner une valeur positive", NotificationManager.Type.Warning);
                        return false;
                    }
                }
                else
                {
                    player.Notify("DailyStorage", "Format incorrect", NotificationManager.Type.Warning);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        #endregion
        #endregion

        #region PLAYERS
        public async void DailyPrestigePanelPlayer(Player player)
        {
            //Query
            List<DailyPrestige_Player> players = await DailyPrestige_Player.QueryAll();

            //Déclaration
            Panel panel = PanelHelper.Create("DailyPrestige - Contributeurs", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelPlayer(player));

            //Corps
            if (players.Count > 0)
            {
                foreach (var p in players)
                {
                    panel.AddTabLine($"", _ =>
                        {
                            DailyPrestigePanelPlayerDetails(player, p);
                        });
                }
                panel.NextButton("Modifier", () => panel.SelectTab());
            }
            else panel.AddTabLine("Aucun contributeurs", _ => { });

            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        public void DailyPrestigePanelPlayerDetails(Player player, DailyPrestige_Player p = null)
        {
            //Déclaration
            Panel panel = PanelHelper.Create($"DailyPrestige - Gestion d'un contributeur", UIPanel.PanelType.TabPrice, player, () => DailyPrestigePanelPlayerDetails(player, p));

            //Corps
            panel.AddTabLine($"{mk.Color("Identité:", mk.Colors.Info)} {p.CharacterFullName}", _ => { });
            panel.AddTabLine($"{mk.Color("Prestige:", mk.Colors.Info)} {p.Prestige}", _ => { });

            panel.AddButton("Upgrade", async _ =>
            {
                p.Prestige += 1;
                if(await p.Save()) player.Notify("DailyPrestige", "Modification enregistrée", NotificationManager.Type.Success);
                else player.Notify("DailyPrestige", "Modification enregistrée", NotificationManager.Type.Error);              
                panel.Refresh();
            });
            panel.AddButton("Downgrade", async _ =>
            {
                p.Prestige -= 1;
                if(p.Prestige < 0) p.Prestige = 0;
                if (await p.Save()) player.Notify("DailyPrestige", "Modification enregistrée", NotificationManager.Type.Success);
                else  player.Notify("DailyPrestige", "Modification enregistrée", NotificationManager.Type.Error);
                panel.Refresh();
            });
            panel.PreviousButton();
            panel.CloseButton();

            //Affichage
            panel.Display();
        }
        #endregion
    }
}
