using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Globalization;

using Newtonsoft.Json;

//https://www.thatsoftwaredude.com/codebytes/14077/how-to-send-an-http-get-request-in-c-using-httpclient

namespace blendosteamsalesdata
{
    public partial class Form1 : Form
    {

        const int COL_DATE = 0;
        const int COL_UNITS = 1;
        const int COL_SALES = 2;

        const string STEAM_GET_DETAILS_URL = "https://partner.steam-api.com/IPartnerFinancialsService/GetDetailedSales/v001/";
        const string STEAM_GET_ALLDATES_URL = "https://partner.steam-api.com/IPartnerFinancialsService/GetChangedDatesForPartner/v001/";

        DateTime[] allSalesDates;

        //List<SalesInfo> salesInfos;

        public Form1()
        {
            InitializeComponent();

            this.FormClosed += MyClosedHandler;

            this.dataGridView1.DefaultCellStyle.Font = new Font("Consolas", 10);

            textBox_apikey.Text = Properties.Settings.Default.web_api_key;
            textBox_appid.Text = Properties.Settings.Default.appid.ToString();
            textBox_startdate.Text = Properties.Settings.Default.datestart;
            textBox_enddate.Text = Properties.Settings.Default.dateend;

            
            allSalesDates = null;

            AddLog_NonInvoked("Press ctrl+c to copy any selected text.");
        }

        private async void ClickGoButton()
        {
            progressBar1.Value = 0;

            

            button_go.Enabled = false;
            textBox_appid.Enabled = false;
            textBox_startdate.Enabled = false;
            textBox_enddate.Enabled = false;
            textBox_apikey.Enabled = false;
            button_todaydate.Enabled = false;
            button_copyclipboard.Enabled = false;

            dataGridView1.Rows.Clear();
            dataGridView1.ClearSelection();

            DateTime starttime = DateTime.Now;

            SalesInfo[] salesInfos = await StartFetch();

            if (salesInfos == null)
            {
                ReenableButtons();
                AddLog_NonInvoked("*** ERROR ***: something went wrong.");
                return;
            }

            //Generate summary.
            DateTime startDate, endDate;
            GetParsedDate(textBox_startdate.Text, out startDate);
            GetParsedDate(textBox_enddate.Text, out endDate);
            double totalDays = (endDate - startDate).TotalDays + 1;

            int totalUnits = 0;
            float totalSales = 0;
            for (int i = 0; i < salesInfos.Length; i++)
            {
                totalUnits += salesInfos[i].netUnits;
                totalSales += salesInfos[i].netSales;
            }

            TimeSpan delta = DateTime.Now.Subtract(starttime);

            AddLog_NonInvoked("Fetch done. ({0} seconds)",  Math.Round(delta.TotalSeconds, 1).ToString());
            AddLog_NonInvoked(" ");

            AddLog_NonInvoked("{0} to {1} | {2} days | {3} units | ${4} | Median units: {5} | Median sales: ${6}", textBox_startdate.Text, textBox_enddate.Text, totalDays.ToString(),
                totalUnits.ToString("N0"), totalSales.ToString("N2"), GetMedianUnits(salesInfos).ToString(), GetMedianSales(salesInfos).ToString("N2"));


            ReenableButtons();
            

            progressBar1.Value = 100;
        }

        private void ReenableButtons()
        {
            button_go.Enabled = true;
            textBox_appid.Enabled = true;
            textBox_startdate.Enabled = true;
            textBox_enddate.Enabled = true;
            textBox_apikey.Enabled = true;
            button_todaydate.Enabled = true;
            button_copyclipboard.Enabled = true;
        }


        private async Task<SalesInfo[]> StartFetch()
        {
            //Sanity checks
            if (string.IsNullOrWhiteSpace(textBox_apikey.Text))
            {
                AddLog_NonInvoked("*** ERROR ***: Web API Key is missing.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(textBox_appid.Text))
            {
                AddLog_NonInvoked("*** ERROR ***: App ID is missing.");
                return null;
            }

            int appidOutput;
            if (!int.TryParse(textBox_appid.Text, out appidOutput))
            {
                AddLog_NonInvoked("*** ERROR ***: App ID is not an integer.");
                return null;
            }

            DateTime datestartOutput;
            if (!GetParsedDate(textBox_startdate.Text, out datestartOutput))
            {
                AddLog_NonInvoked("*** ERROR ***: Start date is not a valid date.");
                return null;
            }

            DateTime dateendOutput;
            if (!GetParsedDate(textBox_enddate.Text, out dateendOutput))
            {
                AddLog_NonInvoked("*** ERROR ***: End date is not a valid date.");
                return null;
            }

            int dateresult = DateTime.Compare(datestartOutput, dateendOutput);
            if (dateresult > 0)
            {
                AddLog_NonInvoked("*** ERROR ***: 'End date' needs to be same or before 'Start date'.");
                return null;
            }

            // end sanity check.


            //Fetch data.
            AddLog_NonInvoked("Fetching. Please wait...");

            //Fetch dates. Only do this the first time this is run.
            if (allSalesDates == null)
            {
                string salesDates = await GetSalesDates(); //Get all dates in which a sale happened.

                //Convert the JSON to C# objects
                SteamSalesDateContainer alldates = JsonConvert.DeserializeObject<SteamSalesDateContainer>(salesDates);
                allSalesDates = alldates.response.dates;
            }

            int desiredAppid = int.Parse(textBox_appid.Text);

            //Iterate over every date.
            List<SalesInfo> salesList = new List<SalesInfo>();
            DateTime[] datesToCheck = GetDateList();
            for (int i = 0; i < datesToCheck.Length; i++)
            {
                float progressbaramount =  (float)i / (float)datesToCheck.Length;
                ProgressbarUpdateInvoked((int)(progressbaramount * 100));

                //Get sales info for a single date.
                string detailedInfo = await GetSalesPerDateSpan(datesToCheck[i]);
                GetDetailedSales_Container singledaySales = JsonConvert.DeserializeObject<GetDetailedSales_Container>(detailedInfo);

                //Iterate over every sale in the one day.
                bool hasSale = false;
                for (int k = 0; k < singledaySales.response.results.Length; k++)
                {
                    if (singledaySales.response.results[k].primary_appid != desiredAppid)
                        continue;
                    
                    //we now have sales info for one day. Add it to the sales list.
                    AddToSalesInfo(datesToCheck[i], desiredAppid,
                        singledaySales.response.results[k].net_units_sold,
                        singledaySales.response.results[k].net_sales_usd,
                        salesList);

                    hasSale = true;
                }

                if (!hasSale)
                {
                    //If day has zero sales, then make a "no sales" entry.
                    AddToSalesInfo(datesToCheck[i], desiredAppid,
                        0,
                        0,
                        salesList);
                }

                //Add to the data grid.
                if (salesList.Count > 0)
                {
                    AddToDatagridview(salesList[salesList.Count - 1].date, salesList[salesList.Count - 1].netUnits, salesList[salesList.Count - 1].netSales);
                }
            }

            return salesList.ToArray();
        }

        private void AddToDatagridview(DateTime date, int units, float sales)
        {
            MethodInvoker mi = delegate () { AddToDatagridview_nonInvoked(date, units, sales); };
            this.Invoke(mi);
        }

        private void AddToDatagridview_nonInvoked(DateTime date, int units, float sales)
        {
            DataGridViewRow row = new DataGridViewRow();
            row.CreateCells(dataGridView1);
            row.Cells[COL_DATE].Value = string.Format("{0}-{1}-{2}", date.Year, date.Month.ToString("D2"), date.Day.ToString("D2"));
            row.Cells[COL_UNITS].Value = units;
            row.Cells[COL_SALES].Value = string.Format("${0}", sales.ToString("N2"));
            dataGridView1.Rows.Add(row);
        }

        private void AddToSalesInfo(DateTime date, int desiredAppID, int netUnits, float netSales, List<SalesInfo> salesList)
        {
            for (int i = 0; i < salesList.Count; i++)
            {
                if (salesList[i].date.Year == date.Year && salesList[i].date.Month == date.Month && salesList[i].date.Day == date.Day)
                {
                    //match
                    salesList[i].netUnits += netUnits;
                    salesList[i].netSales += netSales;
                    return;
                }
            }

            SalesInfo newItem = new SalesInfo();
            newItem.date = date;
            newItem.netUnits += netUnits;
            newItem.netSales += netSales;
            salesList.Add(newItem);
        }

        private DateTime[] GetDateList()
        {
            //Get the user values.
            DateTime startdate;
            GetParsedDate(textBox_startdate.Text, out startdate);

            DateTime enddate;
            GetParsedDate(textBox_enddate.Text, out enddate);

            List<DateTime> datelist = new List<DateTime>();

            //Parse what dates we want to check.
            for (int i = 0; i < allSalesDates.Length; i++)
            {
                if (allSalesDates[i] < startdate || allSalesDates[i] > enddate)
                    continue;

                datelist.Add(allSalesDates[i]);
            }

            return datelist.ToArray();
        }





        //Grab all dates in which a sale was made.
        private async Task<string> GetSalesDates()
        {
            string steamWebApiKey = GetSteamWebAPIKey();

            IList<SteamWebRequestParameter> parameters = new List<SteamWebRequestParameter>();
            parameters.Insert(0, new SteamWebRequestParameter("key", steamWebApiKey));
            parameters.Insert(0, new SteamWebRequestParameter("highwatermark", "0"));

            using (var client = new HttpClient())
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    string command = BuildRequestCommand(STEAM_GET_ALLDATES_URL, parameters);
                    HttpResponseMessage httpResponse = await client.GetAsync(command).ConfigureAwait(false);
                    httpResponse.EnsureSuccessStatusCode();

                    string output = await httpResponse.Content.ReadAsStringAsync();
                    return output;
                }
                catch (Exception err)
                {
                    AddLog("*** ERROR ***");
                    AddLog(err.Message);
                    AddLog(err.InnerException.Message);
                }
            }

            return string.Empty;
        }






        private async Task<string> GetSalesPerDateSpan(DateTime date)
        {
            string output = await GetSalesPerSpecificDate(date);
            return output;
        }

        private async Task<string> GetSalesPerSpecificDate(DateTime date)
        {
            string steamWebApiKey = GetSteamWebAPIKey();

            string datestring = string.Format("{0}-{1}-{2}", date.Year, date.Month.ToString("D2"), date.Day.ToString("D2"));

            IList<SteamWebRequestParameter> parameters = new List<SteamWebRequestParameter>();
            parameters.Insert(0, new SteamWebRequestParameter("key", steamWebApiKey));
            parameters.Insert(0, new SteamWebRequestParameter("date", datestring));
            parameters.Insert(0, new SteamWebRequestParameter("highwatermark_id", "0"));

            using (var client = new HttpClient())
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        
                    string command = BuildRequestCommand(STEAM_GET_DETAILS_URL, parameters);
                    HttpResponseMessage httpResponse = await client.GetAsync(command).ConfigureAwait(false);
                    httpResponse.EnsureSuccessStatusCode();
        
                    string output = await httpResponse.Content.ReadAsStringAsync();
                    return output;
                }
                catch (Exception err)
                {
                    AddLog("*** ERROR ***");
                    AddLog(err.Message);
                    AddLog(err.InnerException.Message);
                }
            }
        
            return string.Empty;
        }

        

        private string GetSteamWebAPIKey()
        {
            return textBox_apikey.Text;
        }



        

        private void AddLog(string text, params string[] args)
        {
            MethodInvoker mi = delegate () { AddLog_NonInvoked(text, args); };
            this.Invoke(mi);
        }

        private void ProgressbarUpdateInvoked(int value)
        {
            MethodInvoker mi = delegate () { ProgressbarUpdate_NonInvoked(value); };
            this.Invoke(mi);
        }

        private void ProgressbarUpdate_NonInvoked(int value)
        {
            progressBar1.Value = value;
        }

        private void AddLog_NonInvoked(string text, params string[] args)
        {
            string displaytext = string.Format(text, args);

            listBox1.Items.Add(displaytext);

            //scroll list down
            int nItems = (int)(listBox1.Height / listBox1.ItemHeight);
            listBox1.TopIndex = listBox1.Items.Count - nItems;

            this.Update();
            this.Refresh();
        }

        protected void MyClosedHandler(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.web_api_key = textBox_apikey.Text;

            int appID;
            if (int.TryParse(textBox_appid.Text, out appID))
            {
                Properties.Settings.Default.appid = appID;
            }

            DateTime date;
            if (GetParsedDate(textBox_startdate.Text, out date))
            {
                Properties.Settings.Default.datestart = textBox_startdate.Text;
            }

            if (GetParsedDate(textBox_enddate.Text, out date))
            {
                Properties.Settings.Default.dateend = textBox_enddate.Text;
            }

            Properties.Settings.Default.Save();
        }

        private bool GetParsedDate(string _text, out DateTime output)
        {
            try
            {
                output = DateTime.ParseExact(_text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                AddLog_NonInvoked("*** ERROR ***: Invalid date value.");
                output = new DateTime();
            }

            return false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            ClickGoButton();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //make the end date be today
            DateTime nowtime = DateTime.Now;

            textBox_enddate.Text = string.Format("{0}-{1}-{2}", nowtime.Year, nowtime.Month.ToString("D2"), nowtime.Day.ToString("D2"));
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            dataGridView1.SelectAll();
            DataObject dataObj = dataGridView1.GetClipboardContent();

            if (dataObj == null)
            {
                return;
            }

            Clipboard.SetDataObject(dataObj, true);
        }

        //detect ctrl+C for listbox
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                if (listBox1.Focused)
                {
                    if (listBox1.SelectedItem != null)
                    {
                        string strToCopy = listBox1.SelectedItem.ToString();
                        CopyToClipboard(strToCopy, false);
                    }

                    return true;
                }

            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void CopyToClipboard(string strToCopy, bool showLog = true)
        {
            try
            {
                Clipboard.SetText(strToCopy);

                if (showLog)
                {
                    AddLog("[COPIED TO CLIPBOARD]: {0}", strToCopy);
                }
            }
            catch
            {
                AddLog("*** ERROR ***: failed to copy to clipboard.");
                //SetListboxColor(Color.Pink);
            }
        }

        private int GetMedianUnits(SalesInfo[] salesInfos)
        {
            if (salesInfos.Length <= 0)
                return 0;

            List<int> unitlist = new List<int>();
            for (int i = 0; i < salesInfos.Length; i++)
            {
                unitlist.Add(salesInfos[i].netUnits);
            }

            unitlist.Sort();
            return unitlist[unitlist.Count / 2];
        }

        private float GetMedianSales(SalesInfo[] salesInfos)
        {
            if (salesInfos.Length <= 0)
                return 0;

            List<float> salesList = new List<float>();
            for (int i = 0; i < salesInfos.Length; i++)
            {
                salesList.Add(salesInfos[i].netSales);
            }

            salesList.Sort();
            return salesList[salesList.Count / 2];
        }

        //from https://github.com/babelshift/SteamWebAPI2
        private string BuildRequestCommand(string interfaceName, IEnumerable<SteamWebRequestParameter> parameters)
        {
            if (string.IsNullOrWhiteSpace(interfaceName))
            {
                throw new ArgumentNullException(nameof(interfaceName));
            }

            if (parameters == null)
            {
                parameters = new List<SteamWebRequestParameter>();
            }

            string commandUrl = $"{interfaceName}";

            // if we have parameters, join them together with & delimiter and append them to the command URL
            if (parameters != null && parameters.Count() > 0)
            {
                string parameterString = string.Join("&", parameters);
                commandUrl += $"?{parameterString}";
            }

            return commandUrl;
        }
    }


    //from https://github.com/babelshift/SteamWebAPI2
    public class SteamWebRequestParameter
    {
        /// <summary>
        /// Name of the parameter (such as "key" in "key=123456" parameter)
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Value of the parameter (such as "123456" in "key=123456" parameter)
        /// </summary>
        public string Value { get; private set; }

        /// <summary>
        /// Constructs a parameter with the given name and value. Name must not be null or empty.
        /// </summary>
        /// <param name="name">Name to give this parameter</param>
        /// <param name="value">Value to give this parameter</param>
        public SteamWebRequestParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException("name");
            }

            Name = name;
            Value = value;
        }

        /// <summary>
        /// Returns a string which concatenates the name and value together with '=' symbol as it would appear in a URL
        /// </summary>
        public override string ToString()
        {
            return $"{Name}={Value}";
        }
    }

    //for Steam GetChangedDatesForPartner
    public class SteamSalesDateContainer
    {
        [JsonProperty("response")]
        public SteamSalesDateSingle response { get; set; }

        public SteamSalesDateContainer()
        {
        }
    }

    public class SteamSalesDateSingle
    {
        [JsonProperty("dates")]
        public DateTime[] dates { get; set; }
    }


    //for Steam GetDetailedSales
    public class GetDetailedSales_Container
    {
        [JsonProperty("response")]
        public GetDetailedSales_ResultsContainer response;
    }

    public class GetDetailedSales_ResultsContainer
    {
        [JsonProperty("results")]
        public GetDetailedSales_DetailedSalesResult[] results;
    }

    public class GetDetailedSales_DetailedSalesResult
    {
        [JsonProperty("primary_appid")]
        public int primary_appid { get; set; }

        [JsonProperty("net_units_sold")]
        public int net_units_sold { get; set; }

        [JsonProperty("net_sales_usd")]
        public float net_sales_usd { get; set; }
    }




    public class SalesInfo
    {
        public DateTime date;
        public int netUnits;
        public float netSales;
    }
}
