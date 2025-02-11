﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Cryptography;
using WalletMiddleware.ApisJava;
using WalletMiddleware.ApisJava.vo;
using System.Text.RegularExpressions;
namespace WalletMiddleware
{
    public partial class Index : Form
    {
        public Index()
        {
            InitializeComponent();
            BindCoinTypeCombobox();
            if (String.IsNullOrEmpty(Settings.Instance.PrivateKey) || String.IsNullOrEmpty(Settings.Instance.WcfUserId))
            {
                lbInfo.Text = "请先配置ApiKey,点击左上角的菜单>全局参数配置";
            }
            else {
                lbInfo.Text = "你可以选择你想要测试的币种，然后发币或者生成地址,如果没有可选择的币种请先到App开通";
            }
            this.MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

        }

         

        private void btnSend_Click(object sender, EventArgs e)
        {
            //接收地址
            string iAddr = tbAddrs.Text.Trim();
            string iAmount = tbAmount.Text.Trim();
            string iGuid = tbGuid.Text.Trim();
           
            string iCoinName = "";
            if (cbCoinList.SelectedValue != null)
            {
                iCoinName = cbCoinList.SelectedValue.ToString();
            }
            if (!string.IsNullOrEmpty(iCoinName) && !string.IsNullOrEmpty(iAddr) && !string.IsNullOrEmpty(iAmount) && !string.IsNullOrEmpty(iGuid))
            {
                //guid
                //string mId = Guid.NewGuid().ToString();
               bool res= ApisJava.JWalletBll.Instane.UploadWithdraw(iCoinName, iAddr, decimal.Parse(iAmount), iGuid);
                if (res)
                {
                    this.tbGuid.Text = Guid.NewGuid().ToString();
                    MessageBox.Show("发币提交成功。");
                }
                else
                {
                    MessageBox.Show("发币提交失败。");

                }

            }
            else
            {

                MessageBox.Show("参数为空值");
            }

        }


        

        #region 开通币

      
        List<ReciveHotWalletVo> hotList = null;
        private void BindCoinTypeCombobox()
        {
            hotList = JWalletBll.Instane.GetHotWallets();
            cbCoinList.ValueMember = "CoinType";
            cbCoinList.DisplayMember = "CoinType";
            cbCoinList.DataSource = hotList;

           this.tbGuid.Text = Guid.NewGuid().ToString();
        }


        #endregion

        private void 全局参数配置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ApiKeySetting apiKeySetting = new ApiKeySetting();
            apiKeySetting.Show();
        }
    }
}
