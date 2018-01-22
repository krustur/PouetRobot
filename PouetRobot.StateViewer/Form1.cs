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

            _allPlatforms.ForEach(x => checkedListPlatforms.Items.Add(x));
            AdjustCheckedListHeight(checkedListPlatforms);

            _allTypes.ForEach(x => checkedListType.Items.Add(x));
            AdjustCheckedListHeight(checkedListType);

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
            treeView1.BeginUpdate();

            // Clear the TreeView each time the method is called.
            treeView1.Nodes.Clear();

            // Add a root TreeNode for each Customer object in the ArrayList.
            var checkedFileTypes = checkedListFileTypes.CheckedItems;
            
            foreach (var group in _allGroups)
            {
                var productions = _allProductions
                    .Where(x => x.Metadata.Groups.Contains(group))
                    .Where(x => checkedFileTypes.Count == 0 || checkedFileTypes.Contains(x.Download.FileType))
                    .OrderBy(x => x.ToString())
                    .ToList();
                if (productions.Count > 0)
                {
                    var groupNode = new TreeNode(group);


                    //Add a child treenode for each Order object in the current Customer object.
                    foreach (var production in productions)
                    {
                        groupNode.Nodes.Add(new TreeNode(production.ToString()));
                    }

                    treeView1.Nodes.Add(groupNode);
                }
            }

            // Reset the cursor to the default for all controls.
            Cursor.Current = Cursors.Default;

            // Begin repainting the TreeView.
            treeView1.EndUpdate();
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
            //e.Node.
        }
    }
}
