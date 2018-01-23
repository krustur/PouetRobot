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
using Serilog.Core;

namespace PouetRobot.StateViewer
{
    public partial class Form1 : Form
    {
        private Logger _logger;
        private List<Production> _allProductions;
        private List<string> _allGroups;
        private List<FileType> _allFileTypes;
        private List<FileIdentifiedByType> _allFileIdentifiedBy;
        private List<MetadataStatus> _allMetadataStatuses;
        private List<DownloadStatus> _allDownloadStatuses;
        private List<string> _allParties;
        private List<string> _allPlatforms;
        private List<string> _allReleaseDates;
        private List<string> _allTypes;

        public Form1()
        {
            InitializeComponent();
            _logger = new LoggerConfiguration()
                .WriteTo.ColoredConsole()
                .WriteTo.RollingFile("PouetRobot.StateViewer{date}.log")
                .CreateLogger();

            _logger.Information("Begin work!");

            var productionsPath = @"D:\Temp\PouetDownload\";
            var webCachePath = @"D:\Temp\PouetDownload\WebCache\";
            var productionsFileName = $@"Productions.json";
            var robot = new Robot(null, productionsPath, productionsFileName, webCachePath, _logger);

            robot.LoadProductions(IndexScanMode.DisableScan);

            _allProductions = robot.Productions.Select(x => x.Value).ToList();

            _allMetadataStatuses = _allProductions.Select(x => x.Metadata.Status).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allGroups = _allProductions.SelectMany(x => x.Metadata.Groups).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allParties = _allProductions.Select(x=> x.Metadata.Party).DistinctBy(x => x).OrderBy(x => x).ToList();

            _allPlatforms = _allProductions.SelectMany(x => x.Metadata.Platforms).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allReleaseDates = _allProductions.Select(x=> x.Metadata.ReleaseDate).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allTypes = _allProductions.SelectMany(x=> x.Metadata.Types).DistinctBy(x => x).OrderBy(x => x).ToList();

            _allDownloadStatuses = _allProductions.Select(x => x.Download.Status).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allFileTypes = _allProductions.Select(x => x.Download.FileType).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allFileIdentifiedBy = _allProductions.Select(x => x.Download.FileIdentifiedByType).DistinctBy(x => x).OrderBy(x => x).ToList();

            _allFileTypes.ForEach(x => checkedListFileTypes.Items.Add(x) );
            AdjustCheckedListHeight(checkedListFileTypes);

            _allPlatforms.Where(x => new List<string>{"Amiga AGA", "Amiga OCS/ECS", "Amiga PPC/RTG" }.Contains(x)).ToList().ForEach(x => checkedListPlatforms.Items.Add(x));
            AdjustCheckedListHeight(checkedListPlatforms);

            _allTypes.ForEach(x => checkedListTypes.Items.Add(x));
            AdjustCheckedListHeight(checkedListTypes);

            //var testREbels = _allProductions.Where(x => x.Group == "Rebels");

            LoadTreeView();
        }

        private void AdjustCheckedListHeight(CheckedListBox checkedListBox)
        {
            var h = checkedListBox.ItemHeight * checkedListBox.Items.Count;
            checkedListBox.Height = h + checkedListBox.Height - checkedListBox.ClientSize.Height;
        }

        private void LoadTreeView()
        {
            // Suppress repainting the TreeView until all the objects have been created.
            treeViewProductions.BeginUpdate();

            // Clear the TreeView each time the method is called.
            treeViewProductions.Nodes.Clear();

            // Add a root TreeNode for each Customer object in the ArrayList.
            var checkedFileTypes = GetCheckedListItemsAsStrings(checkedListFileTypes.CheckedItems);
            var checkedPlatforms = GetCheckedListItemsAsStrings(checkedListPlatforms.CheckedItems);
            var checkedTypes = GetCheckedListItemsAsStrings(checkedListTypes.CheckedItems);
                   
            var filteredProductions = _allProductions
                .FilterFileTypes(checkedFileTypes)
                .FilterPlatforms(checkedPlatforms)
                .FilterTypes(checkedTypes)
                .OrderBy(x => x.Title)
                .ToList()
                ;
            //if (checkedFileTypes.Count > 0)
            //{
            //    filteredProductions = filteredProductions.Where(x => checkedFileTypes.Contains(x.Download.FileType.ToString())).ToList();
            //}
            //if (checkedPlatforms.Count > 0)
            //{
            //    filteredProductions = filteredProductions.Where(x => x.Metadata.Platforms.Any(y => checkedPlatforms.Contains(y))).ToList();
            //}
            //if (checkedTypes.Count > 0)
            //{
            //    filteredProductions = filteredProductions.Where(x => x.Metadata.Types.Any(y => checkedTypes.Contains(y))).ToList();
            //}
            //filteredProductions = filteredProductions.OrderBy(x => x.ToString()).ToList();

            foreach (var group in _allGroups)
            {
                var productions = filteredProductions
                    .Where(x => x.Metadata.Groups.Contains(group))
                    .ToList();
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
                        groupNode.Nodes.Add(prodNode);
                    }

                    treeViewProductions.Nodes.Add(groupNode);
                }
            }

            // Reset the cursor to the default for all controls.
            Cursor.Current = Cursors.Default;

            // Begin repainting the TreeView.
            treeViewProductions.EndUpdate();
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
            LoadTreeView();
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
                textBoxDownloadUrl.Text = production.Metadata.DownloadUrl;

                labelDownloadStatus.Text = $@"[{production.Download.Status.ToString()}]";
                textBoxFileName.Text = production.Download.FileName;
                textBoxFileType.Text = production.Download.FileType.ToString();
                textBoxFileSize.Text = production.Download.FileSize.ToString();
                textBoxFileIdentifiedByType.Text = production.Download.FileIdentifiedByType.ToString();
                textBoxCacheFileName.Text = production.Download.CacheFileName;

            }
        }
    }
}
