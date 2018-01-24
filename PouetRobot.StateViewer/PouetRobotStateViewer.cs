using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;

namespace PouetRobot.StateViewer
{
    public partial class PouetRobotStateViewer : Form
    {
        private readonly IList<Production> _allProductions;
        private readonly IList<string> _allGroups;

        private readonly Robot _robot;
        //private List<FileIdentifiedByType> _allFileIdentifiedBy;
        //private List<string> _allParties;
        //private List<string> _allReleaseDates;

        public PouetRobotStateViewer()
        {
            InitializeComponent();
            var logger = new LoggerConfiguration()
                .WriteTo.ColoredConsole()
                .WriteTo.RollingFile("PouetRobot.StateViewer{date}.log")
                .CreateLogger();

            logger.Information("Begin work!");

            var productionsPath = @"D:\Temp\PouetDownload\";
            var webCachePath = @"D:\Temp\PouetDownload\WebCache\";
            var productionsFileName = $@"Productions.json";
            _robot = new Robot(null, productionsPath, productionsFileName, webCachePath, logger, retryErrorDownloads: false);

            _robot.LoadProductions(IndexScanMode.DisableScan).GetAwaiter().GetResult();

            _allProductions = _robot.Productions.Select(x => x.Value).ToList();

            var allMetadataStatuses = _allProductions.Select(x => x.Metadata.Status).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allGroups = _allProductions.GetGroups();

            //_allParties = _allProductions.Select(x=> x.Metadata.Party).DistinctBy(x => x).OrderBy(x => x).ToList();

            var allPlatforms = _allProductions.SelectMany(x => x.Metadata.Platforms).DistinctBy(x => x).OrderBy(x => x).ToList();
            //_allReleaseDates = _allProductions.Select(x=> x.Metadata.ReleaseDate).DistinctBy(x => x).OrderBy(x => x).ToList();
            var allTypes = _allProductions.SelectMany(x=> x.Metadata.Types).DistinctBy(x => x).OrderBy(x => x).ToList();

            var allDownloadStatuses = _allProductions.Select(x => x.Download.Status).DistinctBy(x => x).OrderBy(x => x).ToList();
            var allFileTypes = _allProductions.Select(x => x.Download.FileType).DistinctBy(x => x).OrderBy(x => x).ToList();
            //_allFileIdentifiedBy = _allProductions.Select(x => x.Download.FileIdentifiedByType).DistinctBy(x => x).OrderBy(x => x).ToList();

            allMetadataStatuses.ForEach(x => checkedListMetadataStatuses.Items.Add(x));
            AdjustCheckedListHeight(checkedListMetadataStatuses);

            allDownloadStatuses.ForEach(x => checkedListDownloadStatuses.Items.Add(x));
            AdjustCheckedListHeight(checkedListDownloadStatuses);

            allFileTypes.ForEach(x => checkedListFileTypes.Items.Add(x) );
            AdjustCheckedListHeight(checkedListFileTypes);

            allPlatforms.Where(x => new List<string>{"Amiga AGA", "Amiga OCS/ECS", "Amiga PPC/RTG" }.Contains(x)).ToList().ForEach(x => checkedListPlatforms.Items.Add(x));
            AdjustCheckedListHeight(checkedListPlatforms);

            allTypes.ForEach(x => checkedListTypes.Items.Add(x));
            AdjustCheckedListHeight(checkedListTypes);

            //var testREbels = _allProductions.Where(x => x.Group == "Rebels");

            LoadPreeViewTreeView();
            LoadProductionsTreeView();
        }

        private void AdjustCheckedListHeight(CheckedListBox checkedListBox)
        {
            var h = checkedListBox.ItemHeight * checkedListBox.Items.Count;
            checkedListBox.Height = h + checkedListBox.Height - checkedListBox.ClientSize.Height;
        }

        private void LoadProductionsTreeView()
        {
            // Suppress repainting the TreeView until all the objects have been created.
            treeViewProductions.BeginUpdate();

            // Clear the TreeView each time the method is called.
            treeViewProductions.Nodes.Clear();

            // Add a root TreeNode for each Customer object in the ArrayList.
            var checkedFileTypes = GetCheckedListItemsAsStrings(checkedListFileTypes.CheckedItems);
            var checkedPlatforms = GetCheckedListItemsAsStrings(checkedListPlatforms.CheckedItems);
            var checkedTypes = GetCheckedListItemsAsStrings(checkedListTypes.CheckedItems);
            var checkedMetadataStatuses = GetCheckedListItemsAsStrings(checkedListMetadataStatuses.CheckedItems);
            var checkedDownloadStatuses = GetCheckedListItemsAsStrings(checkedListDownloadStatuses.CheckedItems);
                   
            var filteredProductions = _allProductions
                .FilterFileTypes(checkedFileTypes)
                .FilterPlatforms(checkedPlatforms)
                .FilterTypes(checkedTypes)
                .FilterMetadataStatuses(checkedMetadataStatuses)
                .FilterDownloadStatuses(checkedDownloadStatuses)
                .OrderBy(x => x.Title)
                .ToList()
                ;
            
            var totalCount = 0;
            foreach (var group in _allGroups)
            {
                var productions = filteredProductions
                    .Where(x => x.Metadata.Groups.Contains(group))
                    .ToList();

                totalCount += productions.Count;

                if (productions.Count > 0)
                {
                    var groupNode = new TreeNode(group);


                    //Add a child treenode for each Order object in the current Customer object.
                    foreach (var production in productions)
                    {
                        var prodNode = new TreeNode(production.ToString())
                        {
                            Name = production.PouetId.ToString()
                        };
                        if (production.Download.Status != DownloadStatus.Ok)
                        {
                            groupNode.ForeColor = Color.OrangeRed;
                            prodNode.ForeColor = Color.OrangeRed;
                        }
                        groupNode.Nodes.Add(prodNode);
                    }

                    treeViewProductions.Nodes.Add(groupNode);
                }
            }

            labelProductionsCount.Text = $@"{filteredProductions.Count} ({totalCount})/{_allProductions.Count}";

            // Reset the cursor to the default for all controls.
            Cursor.Current = Cursors.Default;

            // Begin repainting the TreeView.
            treeViewProductions.EndUpdate();
        }

        private void LoadPreeViewTreeView()
        {
            // Suppress repainting the TreeView until all the objects have been created.
            treeViewPreview.BeginUpdate();

            // Clear the TreeView each time the method is called.
            treeViewPreview.Nodes.Clear();


            var masterFolderLayout = _robot.GetMasterFolderStructure();

            var nodes = CreateTreeViewNodes(masterFolderLayout);
            foreach (var treeNode in nodes)
            {
                treeViewPreview.Nodes.Add(treeNode);
            }

            // Reset the cursor to the default for all controls.
            Cursor.Current = Cursors.Default;

            // Begin repainting the TreeView.
            treeViewPreview.EndUpdate();
        }

        private IList<TreeNode> CreateTreeViewNodes(IList<Folder> folderLayout)
        {
            var nodes = new List<TreeNode>();
            foreach (var folder in folderLayout.OrderBy(x => x.Name))
            {
                var children = CreateTreeViewNodes(folder.Childrens);
                var folderNode = new TreeNode(folder.Name, children.ToArray());
                nodes.Add(folderNode);

                if (folder.Production != null)
                {
                    var fileNode = new TreeNode(folder.Production.Download.FileName);
                    folderNode.Nodes.Add(fileNode);
                    folderNode.Name = folder.Production.PouetId.ToString();
                    fileNode.Name = folder.Production.PouetId.ToString();
                }
            }

            return nodes;
        }

        private IList<string> GetCheckedListItemsAsStrings(CheckedListBox.CheckedItemCollection checkedItems)
        {
            var result = new List<string>();
            foreach (var checkedItem in checkedItems)
            {
                result.Add(checkedItem.ToString());
            }

            return result;
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void buttonUpdate_Click(object sender, EventArgs e)
        {
            LoadProductionsTreeView();
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Name != String.Empty)
            {
                var production = _allProductions.Single(x => x.PouetId.ToString() == e.Node.Name);

                labelMetadataStatus.Text = $@"[{production.Metadata.Status.ToString()}]";
                textBoxTitle.Text = production.Title;
                textBoxGroups.Text = production.Metadata.Groups.ToSingleString();
                textBoxTypes.Text = production.Metadata.Types.ToSingleString();
                textBoxPlatforms.Text = production.Metadata.Platforms.ToSingleString();
                textBoxParty.Text = production.Metadata.Party;
                textBoxPartyYear.Text = production.Metadata.PartyYear;
                textBoxPartyCompo.Text = production.Metadata.PartyCompo;
                textBoxPartyRank.Text = production.Metadata.PartyRank;
                textBoxReleaseDate.Text = production.Metadata.ReleaseDate;
                textBoxRulez.Text = production.Metadata.Rulez.ToString();
                textBoxItsOk.Text = production.Metadata.IsOk.ToString();
                textBoxSucks.Text = production.Metadata.Sucks.ToString();
                textBoxCdcs.Text = production.Metadata.CoupDeCours.ToString();
                textBoxAllTimeRank.Text = production.Metadata.AllTimeRank.ToString();
                textBoxUrl.Text = production.PouetUrl;
                textBoxDownloadUrl.Text = production.Metadata.DownloadUrl;

                labelDownloadStatus.Text = $@"[{production.Download.Status.ToString()}]";
                textBoxFileName.Text = production.Download.FileName;
                textBoxFileType.Text = production.Download.FileType.ToString();
                textBoxFileSize.Text = production.Download.FileSize.ToString();
                textBoxFileIdentifiedByType.Text = production.Download.FileIdentifiedByType.ToString();
                textBoxCacheFileName.Text = production.Download.CacheFileName;

            }
            else
            {
                labelMetadataStatus.Text = "";
                textBoxTitle.Text = "";
                textBoxGroups.Text = "";
                textBoxTypes.Text = "";
                textBoxPlatforms.Text = "";
                textBoxParty.Text = "";
                textBoxPartyYear.Text = "";
                textBoxPartyCompo.Text = "";
                textBoxPartyRank.Text = "";
                textBoxReleaseDate.Text = "";
                textBoxRulez.Text = "";
                textBoxItsOk.Text = "";
                textBoxSucks.Text = "";
                textBoxCdcs.Text = "";
                textBoxAllTimeRank.Text = "";
                textBoxUrl.Text = "";
                textBoxDownloadUrl.Text = "";

                labelDownloadStatus.Text = "";
                textBoxFileName.Text = "";
                textBoxFileType.Text = "";
                textBoxFileSize.Text = "";
                textBoxFileIdentifiedByType.Text = "";
                textBoxCacheFileName.Text = "";
            }
        }

        private void buttonListUnknownHtmlUrls_Click(object sender, EventArgs e)
        {
            var unknownHtml = _allProductions
                .Where(x => x.Download.Status == DownloadStatus.UnknownHtml)
                .OrderBy(x => x.Metadata.DownloadUrl)
                .Select(x => x.Metadata.DownloadUrl)
                .ToList()
                .ToSingleString("\n\r");
            ShowInfoMessage(@"Urls with unknown html", unknownHtml);
                
        }

        private void buttonListUnknownFileTypes_Click(object sender, EventArgs e)
        {
            var unknownHtml = _allProductions
                .Where(x => x.Download.Status == DownloadStatus.UnknownFileType)
                .OrderBy(x => x.Title)
                //.Select(x => $"{x.Title} {x.Download.CacheFileName} [{x.Download.FileSize}]")
                .Select(x => $"{x.ToString()} {x.Download.FileName} [{x.Download.FileSize}] {x.Download.CacheFileName} ")
                .ToList()
                .ToSingleString("\n\r")
                ;
            ShowInfoMessage(@"Urls with unknown html", unknownHtml);
        }

        private void ShowInfoMessage(string title, string infoMessage)
        {
            MessageBox.Show(
                infoMessage,
                title,
                MessageBoxButtons.OK);
        }
    }
}
