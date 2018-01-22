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
        private List<DownloadMetadataStatus> _allDownloadUrlStatus;
        private List<DownloadProductionStatus> _allDownloadProductionStatus;
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

            robot.LoadProductions(false, false);

            _allProductions = robot.Productions.Select(x => x.Value).ToList();
            _allGroups = _allProductions.Select(x=> x.Group).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allFileTypes = _allProductions.Select(x=> x.FileType).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allFileIdentifiedBy = _allProductions.Select(x=> x.FileIdentifiedByType).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allDownloadUrlStatus = _allProductions.Select(x=> x.DownloadMetadataStatus).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allDownloadProductionStatus = _allProductions.Select(x=> x.DownloadProductionStatus).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allParties = _allProductions.Select(x=> x.PartyDescription).DistinctBy(x => x).OrderBy(x => x).ToList();
            var allPlatformsMessy = _allProductions.Select(x=> x.Platform).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allReleaseDates = _allProductions.Select(x=> x.ReleaseDate).DistinctBy(x => x).OrderBy(x => x).ToList();
            _allTypes = _allProductions.Select(x=> x.Type).DistinctBy(x => x).OrderBy(x => x).ToList();

            //var tempPlatforms = new List<string>();
            //foreach (var type in allPlatformsMessy)
            //{
            //    var newType = type
            //        .Replace("BeOS ", "").Replace(" BeOS", "")
            //        .Replace("Acorn ", "").Replace(" Acorn", "")
            //        ;

            //    tempPlatforms.Add(newType);
            //}
            //_allPlatforms = tempPlatforms.DistinctBy(x => x).ToList();

            _allPlatforms = allPlatformsMessy.Select(x => x
                    .Replace("BeOS ", "").Replace(" BeOS", "")
                    .Replace("Acorn ", "").Replace(" Acorn", "")
                    .Replace("Atari Falcon 030 ", "").Replace(" Atari Falcon 030", "")
                    .Replace("Atari ST ", "").Replace(" Atari ST", "")
                    .Replace("Windows ", "").Replace(" Windows", "")
                    .Replace("JavaScript ", "").Replace(" JavaScript", "")
                    .Replace("FreeBSD ", "").Replace(" FreeBSD", "")
                    .Replace("Solaris ", "").Replace(" Solaris", "")
                    .Replace("PocketPC ", "").Replace(" PocketPC", "")
                    .Replace("Mobile Phone ", "").Replace(" Mobile Phone", "")
                    .Replace("Oric ", "").Replace(" Oric", "")
                    .Replace("XBOX 360 ", "").Replace(" XBOX 360", "")
                    .Replace("XBOX ", "").Replace(" XBOX", "")
                    .Replace("GamePark GP2X ", "").Replace(" GamePark GP2X", "")
                    .Replace("GamePark GP32 ", "").Replace(" GamePark GP32", "")
                    .Replace("Raspberry Pi ", "").Replace(" Raspberry Pi", "")
                    .Replace("Android ", "").Replace(" Android", "")
                    .Replace("SEGA Genesis/Mega Drive ", "").Replace(" SEGA Genesis/Mega Drive", "")
                    .Replace("Commodore 64 ", "").Replace(" Commodore 64", "")
                    .Replace("Windows ", "").Replace(" Windows", "")
                    .Replace("Nintendo Wii ", "").Replace(" Nintendo Wii", "")
                    .Replace("Linux ", "").Replace(" Linux", "")
                    .Replace("Flash ", "").Replace(" Flash", "")
                    .Replace("SGI/IRIX ", "").Replace(" SGI/IRIX", "")
                    .Replace("MS-Dos/gus ", "").Replace(" MS-Dos/gus", "")
                    .Replace("MS-Dos ", "").Replace(" MS-Dos", "")
                    .Replace("MacOSX Intel ", "").Replace(" MacOSX Intel", "")
                    .Replace("MacOSX PPC ", "").Replace(" MacOSX PPC", "")
                    .Replace("MacOS ", "").Replace(" MacOS", "")
                    .Replace("Dreamcast ", "").Replace(" Dreamcast", "")
                    .Replace("iOS ", "").Replace(" iOS", "")
                    .Replace("Playstation Portable ", "").Replace(" Playstation Portable", "")
                    .Replace("Playstation 3 ", "").Replace(" Playstation 3", "")
                    .Replace("Playstation ", "").Replace(" Playstation", "")

            ).DistinctBy(x => x).ToList();


            /*
             DownloadMetadataStatus
             DownloadProductionStatus
             FileIdentifiedBy

             FileTypes             
             Platforms
             Types
             */
            _allFileTypes.ForEach(x => checkedListFileTypes.Items.Add(x) );
            AdjustCheckedListHeight(checkedListFileTypes);

            _allPlatforms.ForEach(x => checkedListPlatforms.Items.Add(x));
            AdjustCheckedListHeight(checkedListPlatforms);

            _allTypes.ForEach(x => checkedListType.Items.Add(x));
            AdjustCheckedListHeight(checkedListType);

            var testREbels = _allProductions.Where(x => x.Group == "Rebels");

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
            foreach (string group in _allGroups)
            {
                var productions =  _allProductions
                    .Where(x => x.Group == group)
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
    }
}
