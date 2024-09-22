using Life.Network;
using Life.UI;
using SQLite;
using System.Threading.Tasks;
using ModKit.Helper;
using ModKit.Helper.PointHelper;
using mk = ModKit.Helper.TextFormattingHelper;
using System.Collections.Generic;
using System.Linq;
using Life;
using DailyPrestige.Entities;
using ModKit.Utils;
using Life.InventorySystem;
using DailyPrestige.Classes;

namespace DailyPrestige.Points
{
    internal class Deposit : ModKit.ORM.ModEntity<Deposit>, PatternData
    {
        [AutoIncrement][PrimaryKey] public int Id { get; set; }
        public string TypeName { get; set; }
        public string PatternName { get; set; }

        //Declare your other properties here

        [Ignore] public ModKit.ModKit Context { get; set; }

        public Deposit() { }
        public Deposit(bool isCreated)
        {
            TypeName = nameof(Deposit);
        }

        /// <summary>
        /// Applies the properties retrieved from the database during the generation of a point in the game using this model.
        /// </summary>
        /// <param name="patternId">The identifier of the pattern in the database.</param>
        public async Task SetProperties(int patternId)
        {
            var result = await Query(patternId);

            Id = patternId;
            TypeName = nameof(Deposit);
            PatternName = result.PatternName;
        }

        /// <summary>
        /// Contains the action to perform when a player interacts with the point.
        /// </summary>
        /// <param name="player">The player interacting with the point.</param>
        public void OnPlayerTrigger(Player player)
        {
            DepositPanel(player);
        }

        #region CUSTOM
        public async void DepositPanel(Player player)
        {
            int currentDate = DateUtils.GetNumericalDateOfTheDay();
            List<DailyPrestige_Task> currentTask = await DailyPrestige_Task.Query(t => t.Date == currentDate);

            Panel panel = Context.PanelHelper.Create($"DailyPrestige - Dépôt", UIPanel.PanelType.Text, player, () => DepositPanel(player));

            if (currentTask != null && currentTask.Count > 0)
            {
                string currentSteamId = player.steamId.ToString();
                List<DailyPrestige_Player> currentPlayer = await DailyPrestige_Player.Query(p => p.SteamId == currentSteamId);

                if(currentPlayer != null && currentPlayer.Count > 0)
                {
                    if (currentPlayer[0].LastDateTaskCompleted == currentDate)
                    {
                        panel.TextLines.Add("Merci beaucoup !");
                        panel.TextLines.Add("Vous avez effectué votre donation quotidenne.");
                        panel.TextLines.Add("Demain, nous referons un appel aux dons.");
                    } else
                    {
                        panel.TextLines.Add($"Aujourd'hui, nous soutenons {mk.Color($"\"{currentTask[0].Name}\"", mk.Colors.Orange)}");
                        panel.TextLines.Add($"Apporter {mk.Color($"{currentTask[0].Quantity} {ItemUtils.GetItemById(currentTask[0].ItemId).itemName}", mk.Colors.Orange)} avant minuit.");

                        panel.NextButton("Déposer", async () =>
                        {
                            if(InventoryUtils.CheckInventoryContainsItem(player, currentTask[0].ItemId, currentTask[0].Quantity))
                            {
                                InventoryUtils.RemoveFromInventory(player, currentTask[0].ItemId, currentTask[0].Quantity);
                                currentPlayer[0].Prestige += 1;
                                currentPlayer[0].LastDateTaskCompleted = DateUtils.GetNumericalDateOfTheDay();
                                if (await currentPlayer[0].Save())
                                {
                                    currentTask[0].ResolvedCounter += 1;
                                    await currentTask[0].Save();

                                    var cityHall = Nova.biz.FetchBiz(DailyPrestige._dailyPrestigeConfig.CityHallId);
                                    cityHall.Bank += DailyPrestige._dailyPrestigeConfig.MoneyForCityHall;

                                    player.Notify("DailyPrestige", "Donation effectuée. Vous gagnez 1 point de prestige.", NotificationManager.Type.Success);
                                    panel.Refresh();
                                }
                                else
                                {
                                    player.Notify("DailyPrestige", "Nous n'avons pas pu enregistrer donation", NotificationManager.Type.Error);
                                    panel.Refresh();
                                }
                            }
                            else
                            {
                                player.Notify("DailyPrestige", "Vous n'avez pas les objets demandés", NotificationManager.Type.Success);
                                panel.Refresh();
                            }
                        });
                    }

                    panel.NextButton("Récompenses", () =>
                    {
                        currentPlayer[0].LRewardRecovered = ListConverter.ReadJson(currentPlayer[0].RewardRecovered);
                        DepositRewardPanel(player, currentPlayer[0]);
                    });
                    panel.NextButton("Donateurs", () => DepositLadderPanel(player));
                }
                else
                {
                    panel.TextLines.Add("Devenez un donateur !");
                    panel.TextLines.Add($"{mk.Size("Compléter une donation augmente votre prestige.", 14)}");
                    panel.TextLines.Add($"{mk.Size("Le prestige permet d'acquérir divers récompenses.", 14)}");
                    panel.TextLines.Add($"{mk.Size("Les donations sont quotidiennes", 14)}");

                    panel.AddButton("S'inscrire", async _ =>
                    {
                        DailyPrestige_Player newPlayer = new DailyPrestige_Player();
                        newPlayer.SteamId = player.steamId.ToString();
                        newPlayer.CharacterFullName = player.GetFullName();
                        newPlayer.LRewardRecovered = new List<int>();
                        newPlayer.RewardRecovered = ListConverter.WriteJson(newPlayer.LRewardRecovered);
                        if (await newPlayer.Save())
                        {
                            player.Notify("DailyPrestige", "Inscription enregistrées", NotificationManager.Type.Success);
                            panel.Refresh();
                        }
                        else
                        {
                            player.Notify("DailyPrestige", "Nous n'avons pas pu enregistrer votre inscription", NotificationManager.Type.Error);
                            panel.Refresh();
                        }
                    });
                }
            }
            else panel.TextLines.Add("Aucune tâche n'est disponible");

            panel.CloseButton();

            panel.Display();
        }

        public async void DepositRewardPanel(Player player, DailyPrestige_Player currentPlayer)
        {
            List<DailyPrestige_Reward> query = await DailyPrestige_Reward.QueryAll();
            var rewards = query.OrderBy(p => p.PrestigeRequired);

            Panel panel = Context.PanelHelper.Create($"DailyPrestige - Récompenses", UIPanel.PanelType.TabPrice, player, () => DepositRewardPanel(player, currentPlayer));

            panel.AddTabLine($"{mk.Color($"Prestige:", mk.Colors.Info)} {currentPlayer.Prestige}", _ => { });

            foreach (var reward in rewards)
            {
                Item currentItem = ItemUtils.GetItemById(reward.ItemId);
                bool claimed = currentPlayer.LRewardRecovered.Contains(reward.Id);

                panel.AddTabLine($"{reward.ItemQuantity} x {currentItem.itemName}", $"{(claimed ? $"{mk.Color("récompense récupérée", mk.Colors.Grey)}" : $"requiert prestige {reward.PrestigeRequired}")}", ItemUtils.GetIconIdByItemId(reward.ItemId), async _ =>
                {
                    if(claimed)
                    {
                        player.Notify("DailyPrestige", "Vous avez déjà récupéré cette récompense", NotificationManager.Type.Info);
                        panel.Refresh();
                    } else
                    {
                        if(currentPlayer.Prestige >= reward.PrestigeRequired)
                        {
                            if(InventoryUtils.AddItem(player, reward.ItemId, reward.ItemQuantity))
                            {
                                currentPlayer.LRewardRecovered.Add(reward.Id);
                                currentPlayer.RewardRecovered = ListConverter.WriteJson(currentPlayer.LRewardRecovered);
                                if(await currentPlayer.Save())
                                {
                                    player.Notify("DailyPrestige", $"Vous venez d'obtenir {reward.ItemQuantity} {currentItem.itemName}", NotificationManager.Type.Success);
                                    panel.Refresh();
                                } else
                                {
                                    InventoryUtils.RemoveFromInventory(player, reward.ItemId, reward.ItemQuantity);
                                    player.Notify("DailyPrestige", "Nous n'avons pas pu enregistrer votre récompense", NotificationManager.Type.Error);
                                    panel.Refresh();
                                }
                            } else
                            {
                                player.Notify("DailyPrestige", "Vous n'avez pas suffisament d'espace dans votre inventaire", NotificationManager.Type.Warning);
                                panel.Refresh();
                            }
                        } else
                        {
                            player.Notify("DailyPrestige", "Vous n'avez pas suffisament de prestige", NotificationManager.Type.Info);
                            panel.Refresh();
                        }
                    }
                });
            }

            panel.AddButton("Récupérer", _ => panel.SelectTab());
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }
        public async void DepositLadderPanel(Player player)
        {
            string steamId = player.steamId.ToString();
            List<DailyPrestige_Player> query = await DailyPrestige_Player.QueryAll();
            var players = query.OrderByDescending(p => p.Prestige);

            Panel panel = Context.PanelHelper.Create($"DailyPrestige - Donateurs", UIPanel.PanelType.TabPrice, player, () => DepositLadderPanel(player));

            foreach ((DailyPrestige_Player p, int index) in players.Select((p, index) => (p, index)))
            {
                panel.AddTabLine($"{mk.Color($"{index+1}#", mk.Colors.Warning)} {mk.Color($"{p.CharacterFullName}", (steamId == p.SteamId ? mk.Colors.Info : mk.Colors.Verbose))}", $"Prestige {p.Prestige}", IconUtils.Others.None.Id, _ => { });
            }

            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }
        #endregion

        /// <summary>
        /// Triggers the function to begin creating a new model.
        /// </summary>
        /// <param name="player">The player initiating the creation of the new model.</param>
        public void SetPatternData(Player player)
        {
            //Set the function to be called when a player clicks on the “create new model” button
            SetName(player);
        }
        /// <summary>
        /// Displays all properties of the pattern specified as parameter.
        /// The user can select one of the properties to make modifications.
        /// </summary>
        /// <param name="player">The player requesting to edit the pattern.</param>
        /// <param name="patternId">The ID of the pattern to be edited.</param>
        public async void EditPattern(Player player, int patternId)
        {
            Deposit pattern = new Deposit(false);
            pattern.Context = Context;
            await pattern.SetProperties(patternId);

            Panel panel = Context.PanelHelper.Create($"Modifier un {pattern.TypeName}", UIPanel.PanelType.Tab, player, () => EditPattern(player, patternId));


            panel.AddTabLine($"{mk.Color("Nom:", mk.Colors.Info)} {pattern.PatternName}", _ => {
                pattern.SetName(player, true);
            });

            panel.NextButton("Sélectionner", () => panel.SelectTab());
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }

        /// <summary>
        /// Allows the player to set a name for the pattern, either during creation or modification.
        /// </summary>
        /// <param name="player">The player interacting with the panel.</param>
        /// <param name="inEdition">A flag indicating if the pattern is being edited.</param>
        public void SetName(Player player, bool isEditing = false)
        {
            Panel panel = Context.PanelHelper.Create($"{(!isEditing ? "Créer" : "Modifier")} un modèle de {TypeName}", UIPanel.PanelType.Input, player, () => SetName(player));

            panel.TextLines.Add("Donner un nom à votre boutique");
            panel.inputPlaceholder = "3 caractères minimum";

            if (!isEditing)
            {
                panel.NextButton("Suivant", async () =>
                {
                    if (panel.inputText.Length >= 3)
                    {
                        Deposit newDeposit = new Deposit();
                        newDeposit.TypeName = nameof(Deposit);
                        newDeposit.PatternName = panel.inputText;

                        if (await newDeposit.Save())
                        {
                            player.Notify("DailyPrestige", "Modifications enregistrées", NotificationManager.Type.Success);
                            ConfirmGeneratePoint(player, newDeposit);
                        }
                        else
                        {
                            player.Notify("DailyPrestige", "Nous n'avons pas pu enregistrer vos modifications", NotificationManager.Type.Error);
                            panel.Refresh();
                        }
                    }
                    else
                    {
                        player.Notify("Attention", "Vous devez donner un titre à votre boutique (3 caractères minimum)", Life.NotificationManager.Type.Warning);
                        panel.Refresh();
                    }
                });
            }
            else
            {
                panel.PreviousButtonWithAction("Confirmer", async () =>
                {
                    if (panel.inputText.Length >= 3)
                    {
                        PatternName = panel.inputText;
                        if (await Save()) return true;
                        else
                        {
                            player.Notify("Erreur", "échec lors de la sauvegarde de vos changements", Life.NotificationManager.Type.Error);
                            return false;
                        }
                    }
                    else
                    {
                        player.Notify("Attention", "Vous devez donner un titre à votre boutique (3 caractères minimum)", Life.NotificationManager.Type.Warning);
                        return false;
                    }
                });
            }
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }

        #region REPLACE YOUR CLASS/TYPE AS PARAMETER
        /// <summary>
        /// Displays a panel allowing the player to select a pattern from a list of patterns.
        /// </summary>
        /// <param name="player">The player selecting the pattern.</param>
        /// <param name="patterns">The list of patterns to choose from.</param>
        /// <param name="configuring">A flag indicating if the player is configuring.</param>
        public void SelectPattern(Player player, List<Deposit> patterns, bool configuring)
        {
            Panel panel = Context.PanelHelper.Create("Choisir un modèle", UIPanel.PanelType.Tab, player, () => SelectPattern(player, patterns, configuring));

            foreach (var pattern in patterns)
            {
                panel.AddTabLine($"{pattern.PatternName}", _ => { });
            }
            if (patterns.Count == 0) panel.AddTabLine($"Vous n'avez aucun modèle de {TypeName}", _ => { });

            if (!configuring && patterns.Count != 0)
            {
                panel.CloseButtonWithAction("Confirmer", async () =>
                {
                    if (await Context.PointHelper.CreateNPoint(player, patterns[panel.selectedTab])) return true;
                    else return false;
                });
            }
            else
            {
                panel.NextButton("Modifier", () => {
                    EditPattern(player, patterns[panel.selectedTab].Id);
                });
                panel.NextButton("Supprimer", () => {
                    ConfirmDeletePattern(player, patterns[panel.selectedTab]);
                });
            }

            panel.AddButton("Retour", ui =>
            {
                AAMenu.AAMenu.menu.AdminPointsSettingPanel(player);
            });
            panel.CloseButton();

            panel.Display();
        }

        /// <summary>
        /// Confirms the generation of a point with a previously saved pattern.
        /// </summary>
        /// <param name="player">The player confirming the point generation.</param>
        /// <param name="pattern">The pattern to generate the point from.</param>
        public void ConfirmGeneratePoint(Player player, Deposit pattern)
        {
            Panel panel = Context.PanelHelper.Create($"Modèle \"{pattern.PatternName}\" enregistré !", UIPanel.PanelType.Text, player, () =>
            ConfirmGeneratePoint(player, pattern));

            panel.TextLines.Add($"Voulez-vous générer un point sur votre position avec ce modèle \"{PatternName}\"");

            panel.CloseButtonWithAction("Générer", async () =>
            {
                if (await Context.PointHelper.CreateNPoint(player, pattern)) return true;
                else return false;
            });
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }
        #endregion

        #region DO NOT EDIT
        /// <summary>
        /// Base panel allowing the user to choose between creating a pattern from scratch
        /// or generating a point from an existing pattern.
        /// </summary>
        /// <param name="player">The player initiating the creation or generation.</param>
        public void CreateOrGenerate(Player player)
        {
            Panel panel = Context.PanelHelper.Create($"Créer ou générer un {TypeName}", UIPanel.PanelType.Text, player, () => CreateOrGenerate(player));

            panel.TextLines.Add(mk.Pos($"{mk.Align($"{mk.Color("Générer", mk.Colors.Info)} utiliser un modèle existant. Les données sont partagés entre les points utilisant un même modèle.", mk.Aligns.Left)}", 5));
            panel.TextLines.Add("");
            panel.TextLines.Add($"{mk.Align($"{mk.Color("Créer:", mk.Colors.Info)} définir un nouveau modèle de A à Z.", mk.Aligns.Left)}");

            panel.NextButton("Créer", () =>
            {
                SetPatternData(player);
            });
            panel.NextButton("Générer", async () =>
            {
                await GetPatternData(player, false);
            });
            panel.AddButton("Retour", ui =>
            {
                AAMenu.AAMenu.menu.AdminPointsPanel(player);
            });
            panel.CloseButton();

            panel.Display();
        }

        /// <summary>
        /// Retrieves all patterns before redirecting to a panel allowing the user various actions (CRUD).
        /// </summary>
        /// <param name="player">The player initiating the retrieval of pattern data.</param>
        /// <param name="configuring">A flag indicating if the user is configuring.</param>
        public async Task GetPatternData(Player player, bool configuring)
        {
            var patterns = await QueryAll();
            SelectPattern(player, patterns, configuring);
        }

        /// <summary>
        /// Confirms the deletion of the specified pattern.
        /// </summary>
        /// <param name="player">The player confirming the deletion.</param>
        /// <param name="patternData">The pattern data to be deleted.</param>
        public async void ConfirmDeletePattern(Player player, PatternData patternData)
        {
            var pattern = await Query(patternData.Id);

            Panel panel = Context.PanelHelper.Create($"Supprimer un modèle de {pattern.TypeName}", UIPanel.PanelType.Text, player, () =>
            ConfirmDeletePattern(player, patternData));

            panel.TextLines.Add($"Cette suppression entrainera également celle des points.");
            panel.TextLines.Add($"Êtes-vous sûr de vouloir supprimer le modèle \"{pattern.PatternName}\" ?");

            panel.PreviousButtonWithAction("Confirmer", async () =>
            {
                if (await Context.PointHelper.DeleteNPointsByPattern(player, pattern))
                {
                    if (await pattern.Delete())
                    {
                        return true;
                    }
                    else
                    {
                        player.Notify("Erreur", $"Nous n'avons pas pu supprimer le modèle \"{PatternName}\"", Life.NotificationManager.Type.Error, 6);
                        return false;
                    }
                }
                else
                {
                    player.Notify("Erreur", "Certains points n'ont pas pu être supprimés.", Life.NotificationManager.Type.Error, 6);
                    return false;
                }
            });
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }

        /// <summary>
        /// Retrieves all NPoints before redirecting to a panel allowing various actions by the user.
        /// </summary>
        /// <param name="player">The player retrieving the NPoints.</param>
        public async Task GetNPoints(Player player)
        {
            var points = await NPoint.Query(e => e.TypeName == nameof(Deposit));
            SelectNPoint(player, points);
        }

        /// <summary>
        /// Lists the points using this pattern.
        /// </summary>
        /// <param name="player">The player selecting the points.</param>
        /// <param name="points">The list of points to choose from.</param>
        public async void SelectNPoint(Player player, List<NPoint> points)
        {
            var patterns = await QueryAll();
            Panel panel = Context.PanelHelper.Create($"Points de type {nameof(Deposit)}", UIPanel.PanelType.Tab, player, () => SelectNPoint(player, points));

            if (points.Count > 0)
            {
                foreach (var point in points)
                {
                    var currentPattern = patterns.FirstOrDefault(p => p.Id == point.PatternId);
                    panel.AddTabLine($"point n° {point.Id}: {(currentPattern != default ? currentPattern.PatternName : "???")}", _ => { });
                }

                panel.NextButton("Voir", () =>
                {
                    DisplayNPoint(player, points[panel.selectedTab]);
                });
                panel.NextButton("Supprimer", async () =>
                {
                    await Context.PointHelper.DeleteNPoint(points[panel.selectedTab]);
                    await GetNPoints(player);
                });
            }
            else
            {
                panel.AddTabLine($"Aucun point de ce type", _ => { });
            }
            panel.AddButton("Retour", ui =>
            {
                AAMenu.AAMenu.menu.AdminPointsSettingPanel(player);
            });
            panel.CloseButton();

            panel.Display();
        }

        /// <summary>
        /// Displays the information of a point and allows the user to modify it.
        /// </summary>
        /// <param name="player">The player viewing the point information.</param>
        /// <param name="point">The point to display information for.</param>
        public async void DisplayNPoint(Player player, NPoint point)
        {
            var pattern = await Query(p => p.Id == point.PatternId);
            Panel panel = Context.PanelHelper.Create($"Point n° {point.Id}", UIPanel.PanelType.Tab, player, () => DisplayNPoint(player, point));

            panel.AddTabLine($"Type: {point.TypeName}", _ => { });
            panel.AddTabLine($"Modèle: {(pattern[0] != null ? pattern[0].PatternName : "???")}", _ => { });
            panel.AddTabLine($"", _ => { });
            panel.AddTabLine($"Position: {point.Position}", _ => { });


            panel.AddButton("TP", ui =>
            {
                Context.PointHelper.PlayerSetPositionToNPoint(player, point);
            });
            panel.AddButton("Définir pos.", async ui =>
            {
                await Context.PointHelper.SetNPointPosition(player, point);
                panel.Refresh();
            });
            panel.PreviousButton();
            panel.CloseButton();

            panel.Display();
        }
        #endregion
    }
}
