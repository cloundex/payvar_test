﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using WalletContracts.Entity;
using WalletMiddleware.TableModels;
using System.Threading;
using WalletMiddleware.Apis.Wcf;
using WalletMiddleware.CheckData;
using WalletMiddleware.Apis;

namespace WalletMiddleware.Dal
{
    public class WithdrawListDal : DbBaseRiskCheck<TableModels.WithdrawList>
    {
        public static readonly WithdrawListDal Instance = new WithdrawListDal();

        public bool IsHave(int vid)
        {
            return Exists(string.Format("VirtualcaptualoperationId={0}", vid));
        }

        public bool IsSendMobiles(int vid)
        {
            return Exists(string.Format("VirtualcaptualoperationId={0} and IsErrReport={1}", vid, 1));
        }

        public void SetHaveSendMobiles(int vid, int Version)
        {
            using (var connection = GetConn)
            {
                string sql = string.Format("update withdrawlist set IsErrReport={0} where VirtualcaptualoperationId={1} ;", 1, vid);
                int rz = connection.Execute(sql);
                if (rz == 0)
                {
                    XS.Core.Log.InfoLog.InfoFormat("数据表：withdrawlist，VirtualcaptualoperationId:{0} 无法更新状态，发生版本冲突！", vid);
                }
            }
        }


        private (bool, string) SendTran()
        {
            int count = 10;
            StringBuilder sbr = new StringBuilder();
            int sum = 0;
            int successcount = 0;
            int falsecount = 0;
            int error = 0;
            do
            {
                var withdrawList = GetList("state=0 limit " + count);
                if (withdrawList.Any())
                {
                    sum += withdrawList.Count();
                    int i = 0;
                    foreach (var item in withdrawList)
                    {
                        if (i > 0)
                        {
                            Thread.Sleep(1000);
                        }
                        i++;
                        string coinType = AppStaticData.CoinTypes[item.CoinTypeId].fShortName;
                        //item.VirtualcaptualoperationId
                        DataReturn<TransactionResult> rzData = Wcf.Instance.SendTransaction(item.Amount, coinType, item.Fees, item.FromAddress, item.WithdrawAddress, item.VirtualcaptualoperationId);
                        //2019-9-17  State 5.只要发送给钱包就设置为 5
                        item.State = 5;
                        Update(item);

                        if (rzData.IsSucess) //api调用成功
                        {
                            if (rzData.Data.IsSuccess)//发币成功
                            {
                                //2020-4-27 这个新币奇葩
                                if (rzData.Data.Hash == "wait")
                                {
                                    item.State = 0;
                                    Update(item);
                                }
                                else
                                {
                                    string sRzInfo = string.Format("成功发送一笔热提币,用户Id:{0},币种:{1},金额:{2},手续费{3},目标地址:{4},发送地址:{5}",
                                        item.UserId, coinType, item.Amount, item.Fees, item.WithdrawAddress, item.FromAddress);
                                    XS.Core.Log.InfoLog.Info(sRzInfo);
                                    sbr.AppendFormat("成功发送一笔热提币:{0}", DateTime.Now);
                                    item.State = 1;
                                    item.TxId = rzData.Data.Hash;

                                    item.Fees = rzData.Data.Fee;
                                    Update(item);


                                    string emailTitle = string.Format("成功发送一笔热提币:{0}", DateTime.Now);
                                    Dal.SendMsgsDal.Instane.SendEmails(emailTitle, sRzInfo);
                                    successcount++;
                                }
                            }
                            else
                            {
                                item.State = 2;
                                Update(item);
                                falsecount++;
                                sbr.AppendFormat("热提币发币失败:{0}", rzData.Msg);
                                Dal.SendMsgsDal.Instane.SendEmails("热提币发币失败!", rzData.Msg);

                                //2019-11-21 yhl 调用wcf发币失败要发短信
                                string sRz = string.Format("有提币操作，但服务返回失败,执行时间:{0} ", DateTime.Now);
                                Dal.SendMsgsDal.Instane.SendMobiles(sRz);
                            }
                        }
                        else
                        {
                            error++;
                            sbr.AppendFormat("热发币调用失败:{0}", rzData.Msg);
                            Dal.SendMsgsDal.Instane.SendEmails("有提现记录，但热发币调用失败!", rzData.Msg);
                        }
                    }
                }
                else
                {
                    break;
                }

            } while (true);

            return (successcount > 0, $"总提币数据{sum}条,发送成功{successcount}条,发送失败{falsecount},调用WCF异常{error}次.");
        }

        public (bool isok, string rzinfo) SendTransaction(int ignoreCoinId)
        {
            bool isucces = false;
            StringBuilder sbRZ = new StringBuilder();
            //查询是否存在代发数据
            var notSendList = GetCount("state = 0 limit 1");
            if (notSendList > 0)
            {
                //是 直接发送至结束
                var (isok, result) = this.SendTran();
                isucces = isok;
                if (isok)
                {
                    sbRZ.Append(result);
                }
                else
                {
                    sbRZ.Append("数据库中暂无发币记录");
                }
            }
            else
            {
                //否  向交易所获取待处理提现记录
                var withdraw = Api.Instance.GetWithdrawList();

                if (withdraw.Any())
                {
                    foreach (var x in withdraw)
                    {
                        //2020-10-27 排除 GRTC
                        if (x.fVi_fId2 != ignoreCoinId && AppStaticData.CoinTypes.ContainsKey(x.fVi_fId2) && AppStaticData.HotWallets.ContainsKey(x.fVi_fId2))
                        {
                            //btc usdt类的交易验证
                            var canSend = CheckCoinTxState.Current.CanSend(x.fVi_fId2, x.withdraw_virtual_address);
                            if (!canSend) continue;

                            //hash验证
                            if (x.CheckMdwu())
                            {
                                //判断是否存在待处理提现记录
                                if (!WithdrawListDal.Instance.IsHave(x.fId))
                                {
                                    // 添加到本地待处理提现记录
                                    WithdrawList mdWithdraw = new WithdrawList();
                                    mdWithdraw.AddTime = DateTime.Now;
                                    mdWithdraw.Amount = x.fAmountPay;
                                    mdWithdraw.CoinTypeId = x.fVi_fId2;
                                    mdWithdraw.Fees = x.ffees;
                                    mdWithdraw.UserId = x.FUs_fId2;
                                    mdWithdraw.WithdrawAddress = x.withdraw_virtual_address;
                                    mdWithdraw.FromAddress = AppStaticData.HotWallets[x.fVi_fId2].Address;
                                    mdWithdraw.VirtualcaptualoperationId = x.fId;
                                    mdWithdraw.State = 0;
                                    Add(mdWithdraw);
                                }
                                else
                                {
                                    // 预警
                                    if (!IsSendMobiles(x.fId))
                                    {
                                        //string msg = string.Format("请注意,有重复提现的订单,ID为{0}提币已经发送", x.fId);
                                        //SendMsgsDal.Instane.SendEmails("请注意,有重复提现的订单", msg);
                                        //XS.Core.Log.InfoLog.Info(msg);
                                        sbRZ.AppendFormat("请注意,ID为{0}提币已经发送", x.fId);

                                        //Dal.SendMsgsDal.Instane.SendEmailAndMobiles("请注意，有提币记录偿试重复发送!", sbRZ.ToString());
                                        var withEntity = GetEntityByWhere(string.Format("VirtualcaptualoperationId={0}", x.fId));
                                        SetHaveSendMobiles(x.fId, withEntity.Version);
                                    }
                                }
                            }
                            else
                            {
                                string sInfo = string.Format("有提币记录，但hash验证错误,记录Id:{0},提币地址:{1}", x.fId, x.withdraw_virtual_address);
                                XS.Core.Log.InfoLog.Error(sInfo);

                                Dal.SendMsgsDal.Instane.SendEmailAndMobiles("有提币记录，但hash验证错误!", sInfo);
                                sbRZ.Append("有提币记录，但hash验证错误!");
                            }
                        }
                    }
                    //查询待提现记录
                    var (isok, result) = this.SendTran();
                    isucces = isok;
                    if (isok)
                    {
                        sbRZ.Append(result);
                    }
                    else
                    {
                        sbRZ.Append("数据库中暂无发币记录");
                    }
                }
                else
                {
                    isucces = true;
                }
            }
            return (isucces, sbRZ.ToString());
        }


        public bool BatchUpdate(string ids, int state = 3)
        {
            if (ids.Length > 0)
            {
                using (var connection = GetConn)
                {
                    string sql = $"update withdrawlist set state={state} where id in ({ids})";

                    int rz = connection.Execute(sql);
                    if (rz == 0)
                    {
                        XS.Core.Log.InfoLog.InfoFormat("修改{0}数据回传发送状态失败", ids);
                        return false;
                    }
                    return true;
                }
            }
            return true;
        }
    }
}
