﻿/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MCost
 * Purpose        : Cost calculation
 * Class Used     : X_M_Cost
 * Chronological    Development
 * Raghunandan     15-Jun-2009
  ******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using System.Windows.Forms;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Logging;

namespace VAdvantage.Model
{
    public class MCost : X_M_Cost
    {
        //	Logger	
        private static VLogger _log = VLogger.GetVLogger(typeof(MCost).FullName);
        // Data is entered Manually		
        private bool _manual = true;

        /* 	Retrieve/Calculate Current Cost Price
        *	@param product product
        *	@param M_AttributeSetInstance_ID real asi
        *	@param as1 accounting schema	
        *	@param AD_Org_ID real org																													
        *	@param costingMethod AcctSchema.COSTINGMETHOD_*
        *	@param qty qty
        *	@param C_OrderLine_ID optional order line
        *	@param zeroCostsOK zero/no costs are OK
        *	@param trxName trx
        *	@return current cost price or null
        */
        public static Decimal GetCurrentCost(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID, String costingMethod, Decimal qty, int C_OrderLine_ID,
            bool zeroCostsOK, Trx trxName)
        {
            String CostingLevel = as1.GetCostingLevel();
            MProductCategoryAcct pca = MProductCategoryAcct.Get(product.GetCtx(),
                product.GetM_Product_Category_ID(), as1.GetC_AcctSchema_ID(), null);
            if (pca == null)
                throw new Exception("Cannot find Acct for M_Product_Category_ID="
                    + product.GetM_Product_Category_ID()
                    + ", C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID());
            //	Costing Level
            if (pca.GetCostingLevel() != null)
                CostingLevel = pca.GetCostingLevel();
            if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_Client.Equals(CostingLevel))
            {
                AD_Org_ID = 0;
                M_AttributeSetInstance_ID = 0;
            }
            else if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_Organization.Equals(CostingLevel))
                M_AttributeSetInstance_ID = 0;
            else if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_BatchLot.Equals(CostingLevel))
                AD_Org_ID = 0;
            //	Costing Method
            if (costingMethod == null)
            {
                costingMethod = pca.GetCostingMethod();
                if (costingMethod == null)
                {
                    costingMethod = as1.GetCostingMethod();
                    if (costingMethod == null)
                        throw new ArgumentException("No Costing Method");
                    //		costingMethod = VAdvantage.Model.MAcctSchema.COSTINGMETHOD_StandardCosting;
                }
            }

            //	Create/Update Costs
            MCostDetail.ProcessProduct(product, trxName);

            return Util.GetValueOfDecimal(GetCurrentCost(
                product, M_AttributeSetInstance_ID,
                as1, AD_Org_ID, as1.GetM_CostType_ID(), costingMethod, qty,
                C_OrderLine_ID, zeroCostsOK, trxName));
        }

        /**
         * 	Get Current Cost Price for Costing Level
         *	@param product product
         *	@param M_ASI_ID costing level asi
         *	@param Org_ID costing level org
         *	@param M_CostType_ID cost type
         *	@param as1 AcctSchema
         *	@param costingMethod method
         *	@param qty quantity
         *	@param C_OrderLine_ID optional order line
         *	@param zeroCostsOK zero/no costs are OK
         *	@param trxName trx
         *	@return cost price or null
         */
        private static Decimal? GetCurrentCost(MProduct product, int M_ASI_ID,
            VAdvantage.Model.MAcctSchema as1, int Org_ID, int M_CostType_ID,
            String costingMethod, Decimal qty, int C_OrderLine_ID,
            bool zeroCostsOK, Trx trxName)
        {
            /**	Any Transactions not costed		*
            String sql1 = "SELECT * FROM M_Transaction t " 
                + "WHERE t.M_Product_ID=?"
                + " AND NOT EXISTS (SELECT * FROM M_CostDetail cd "
                    + "WHERE t.M_Product_ID=cd.M_Product_ID"
                    + " AND (t.M_InOutLine_ID=cd.M_InOutLine_ID))";
            PreparedStatement pstmt1 = null;
            List<MTransaction> list = new List<MTransaction>();
            try
            {
                pstmt1 = DataBase.prepareStatement (sql1, null);.
                pstmt1.setInt (1, product.getM_Product_ID());
                ResultSet dr = pstmt1.executeQuery ();
                while (dr.next ())
                {
                    MTransaction trx = new MTransaction(product.GetCtx(), dr, null);
                    list.Add (trx);
                }
                dr.close ();
                pstmt1.close ();
                pstmt1 = null;
            }
            catch (Exception e)
            {
                s_log.log (Level.SEVERE, sql1, e);
            }
            try
            {
                if (pstmt1 != null)
                    pstmt1.close ();
                pstmt1 = null;
            }
            catch (Exception e)
            {
                pstmt1 = null;
            }
            /**	*/

            //	**
            Decimal? currentCostPrice = null;
            String costElementType = null;
            int M_CostElement_ID = 0;
            Decimal? percent = null;
            //
            Decimal materialCostEach = Env.ZERO;
            Decimal otherCostEach = Env.ZERO;
            Decimal percentage = Env.ZERO;
            int count = 0;
            //
            String sql = "SELECT SUM(c.CurrentCostPrice), ce.CostElementType, ce.CostingMethod,"
                + " NVL(c.PercentCost,0), c.M_CostElement_ID "					//	4..5
                + "FROM M_Cost c"
                + " LEFT OUTER JOIN M_CostElement ce ON (c.M_CostElement_ID=ce.M_CostElement_ID) "
                + "WHERE c.AD_Client_ID=" + product.GetAD_Client_ID() + " AND c.AD_Org_ID=" + Org_ID		//	#1/2
                + " AND c.M_Product_ID=" + product.GetM_Product_ID()							//	#3
                + " AND (c.M_AttributeSetInstance_ID=" + M_ASI_ID + " OR c.M_AttributeSetInstance_ID=0)"	//	#4
                + " AND c.M_CostType_ID=" + M_CostType_ID + " AND c.C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID()	//	#5/6
                + " AND (ce.CostingMethod IS NULL OR ce.CostingMethod=@costingMethod) "	//	#7
                + "GROUP BY ce.CostElementType, ce.CostingMethod, c.PercentCost, c.M_CostElement_ID";
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                SqlParameter[] param = new SqlParameter[1];
                param[0] = new SqlParameter("@costingMethod", costingMethod);
                idr = DB.ExecuteReader(sql, param, trxName);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    currentCostPrice = Convert.ToDecimal(dr[0]);//.getBigDecimal(1);
                    costElementType = dr[1].ToString();
                    String cm = dr[2].ToString();
                    percent = Convert.ToDecimal(dr[3]);
                    M_CostElement_ID = Convert.ToInt32(dr[4]);
                    _log.Finest("CurrentCostPrice=" + currentCostPrice
                        + ", CostElementType=" + costElementType
                       + ", CostingMethod=" + cm
                        + ", Percent=" + percent
                        + ", M_CostElement_ID=" + M_CostElement_ID);

                    if (currentCostPrice != null && Env.Signum((Decimal)currentCostPrice) != 0)
                    {
                        if (cm != null)
                            materialCostEach = Decimal.Add(materialCostEach, (Decimal)currentCostPrice);
                        else
                            otherCostEach = Decimal.Add(otherCostEach, (Decimal)currentCostPrice);
                    }
                    if (percent != null && Env.Signum((Decimal)percent) != 0)
                        percentage = Decimal.Add(percentage, (Decimal)percent);
                    count++;
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            if (count > 1)	//	Print summary
            {
                _log.Finest("MaterialCost=" + materialCostEach
                    + ", OtherCosts=" + otherCostEach
                    + ", Percentage=" + percentage);
            }

            //	Seed Initial Costs
            if (Env.Signum(materialCostEach) == 0)		//	no costs
            {
                if (zeroCostsOK)
                {
                    return Env.ZERO;
                }
                materialCostEach = Util.GetValueOfDecimal(GetSeedCosts(product, M_ASI_ID,
                    as1, Org_ID, costingMethod, C_OrderLine_ID));
            }
            if (materialCostEach == null || materialCostEach == 0)
            {
                return null;
            }

            //	Material Costs
            Decimal materialCost = Decimal.Multiply(materialCostEach, qty);
            //	Standard costs - just Material Costs
            if (VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(costingMethod))
            {
                _log.Finer("MaterialCosts = " + materialCost);
                return materialCost;
            }
            if (VAdvantage.Model.MCostElement.COSTINGMETHOD_Fifo.Equals(costingMethod)
                || VAdvantage.Model.MCostElement.COSTINGMETHOD_Lifo.Equals(costingMethod))
            {
                VAdvantage.Model.MCostElement ce = VAdvantage.Model.MCostElement.GetMaterialCostElement(as1, costingMethod);
                materialCost = Util.GetValueOfDecimal(MCostQueue.GetCosts(product, M_ASI_ID, as1, Org_ID, ce, qty, trxName));
            }

            //	Other Costs
            Decimal otherCost = Decimal.Multiply(otherCostEach, qty);

            //	Costs
            Decimal costs = Decimal.Add(otherCost, materialCost);
            if (Env.Signum(costs) == 0)
            {
                return null;
            }

            _log.Finer("Sum Costs = " + costs);
            int precision = as1.GetCostingPrecision();
            if (Env.Signum(percentage) == 0)	//	no percentages
            {
                if (Env.Scale(costs) > precision)
                {
                    costs = Decimal.Round(costs, precision, MidpointRounding.AwayFromZero);
                    //costs = costs.setScale(precision, Decimal.ROUND_HALF_UP);
                }
                return costs;
            }
            //
            Decimal percentCost = Decimal.Multiply(costs, percentage);
            //percentCost = percentCost.divide(Env.ONEHUNDRED, precision, Decimal.ROUND_HALF_UP);
            percentCost = Decimal.Divide(percentCost, Decimal.Round(Env.ONEHUNDRED, precision, MidpointRounding.AwayFromZero));
            costs = Decimal.Add(costs, percentCost);
            if (Env.Scale(costs) > precision)
            {
                //costs = costs.setScale(precision, Decimal.ROUND_HALF_UP);
                costs = Decimal.Round(costs, precision, MidpointRounding.AwayFromZero);
            }
            _log.Finer("Sum Costs = " + costs + " (Add=" + percentCost + ")");
            return costs;
        }

        /**
         * 	Get Seed Costs
         *	@param product product
         *	@param M_ASI_ID costing level asi
         *	@param as1 accounting schema
         *	@param Org_ID costing level org
         *	@param costingMethod costing method
         *	@param C_OrderLine_ID optional order line
         *	@return price or null
         */
        public static Decimal? GetSeedCosts(MProduct product, int M_ASI_ID,
            VAdvantage.Model.MAcctSchema as1, int Org_ID, String costingMethod, int C_OrderLine_ID)
        {
            Decimal? retValue = null;
            //	Direct Data
            if (VAdvantage.Model.MCostElement.COSTINGMETHOD_AverageInvoice.Equals(costingMethod))
                retValue = CalculateAverageInv(product, M_ASI_ID, as1, Org_ID);
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_AveragePO.Equals(costingMethod))
                retValue = CalculateAveragePO(product, M_ASI_ID, as1, Org_ID);
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_Fifo.Equals(costingMethod))
                retValue = CalculateFiFo(product, M_ASI_ID, as1, Org_ID);
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_Lifo.Equals(costingMethod))
                retValue = CalculateLiFo(product, M_ASI_ID, as1, Org_ID);
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_LastInvoice.Equals(costingMethod))
                retValue = GetLastInvoicePrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_LastPOPrice.Equals(costingMethod))
            {
                if (C_OrderLine_ID != 0)
                    retValue = GetPOPrice(product, C_OrderLine_ID, as1.GetC_Currency_ID());
                if (retValue == null || Env.Signum((Decimal)retValue) == 0)
                    retValue = GetLastPOPrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
            }
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(costingMethod))
            {
                //	migrate old costs
                MProductCosting pc = MProductCosting.Get(product.GetCtx(), product.GetM_Product_ID(),
                    as1.GetC_AcctSchema_ID(), null);
                if (pc != null)
                    retValue = pc.GetCurrentCostPrice();
            }
            else if (VAdvantage.Model.MCostElement.COSTINGMETHOD_UserDefined.Equals(costingMethod))
            {
                ;
            }
            else
                throw new ArgumentException("Unknown Costing Method = " + costingMethod);
            if (retValue != null && Env.Signum((Decimal)retValue) != 0)
            {
                _log.Fine(product.GetName() + ", CostingMethod=" + costingMethod + " - " + retValue);
                return retValue;
            }

            //	Look for exact Order Line
            if (C_OrderLine_ID != 0)
            {
                retValue = GetPOPrice(product, C_OrderLine_ID, as1.GetC_Currency_ID());
                if (retValue != null && Env.Signum((Decimal)retValue) != 0)
                {
                    _log.Fine(product.GetName() + ", VAdvantage.Model.PO - " + retValue);
                    return retValue;
                }
            }

            //	Look for Standard Costs first
            if (!VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(costingMethod))
            {
                VAdvantage.Model.MCostElement ce = VAdvantage.Model.MCostElement.GetMaterialCostElement(as1, VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting);
                MCost cost = Get(product, M_ASI_ID, as1, Org_ID, ce.GetM_CostElement_ID());
                if (cost != null && Env.Signum(cost.GetCurrentCostPrice()) != 0)
                {
                    _log.Fine(product.GetName() + ", Standard - " + retValue);
                    return cost.GetCurrentCostPrice();
                }
            }

            //	We do not have a price
            //	VAdvantage.Model.PO first
            if (VAdvantage.Model.MCostElement.COSTINGMETHOD_AveragePO.Equals(costingMethod)
                || VAdvantage.Model.MCostElement.COSTINGMETHOD_LastPOPrice.Equals(costingMethod)
                || VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(costingMethod))
            {
                //	try Last VAdvantage.Model.PO
                retValue = GetLastPOPrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
                if (Org_ID != 0 && (retValue == null || Env.Signum(System.Convert.ToDecimal(retValue)) == 0))
                    retValue = GetLastPOPrice(product, M_ASI_ID, 0, as1.GetC_Currency_ID());
                if (retValue != null && Env.Signum(System.Convert.ToDecimal(retValue)) != 0)
                {
                    _log.Fine(product.GetName() + ", LastPO = " + retValue);
                    return retValue;
                }
            }
            else	//	Inv first
            {
                //	try last Inv
                retValue = GetLastInvoicePrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
                if (Org_ID != 0 && (retValue == null || Env.Signum((Decimal)retValue) == 0))
                    retValue = GetLastInvoicePrice(product, M_ASI_ID, 0, as1.GetC_Currency_ID());
                if (retValue != null && Env.Signum(System.Convert.ToDecimal(retValue)) != 0)
                {
                    _log.Fine(product.GetName() + ", LastInv = " + retValue);
                    return retValue;
                }
            }

            //	Still Nothing
            //	Inv second
            if (VAdvantage.Model.MCostElement.COSTINGMETHOD_AveragePO.Equals(costingMethod)
                || VAdvantage.Model.MCostElement.COSTINGMETHOD_LastPOPrice.Equals(costingMethod)
                || VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(costingMethod))
            {
                //	try last Inv
                retValue = GetLastInvoicePrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
                if (Org_ID != 0 && (retValue == null || Env.Signum(System.Convert.ToDecimal(retValue)) == 0))
                    retValue = GetLastInvoicePrice(product, M_ASI_ID, 0, as1.GetC_Currency_ID());
                if (retValue != null && Env.Signum(System.Convert.ToDecimal(retValue)) != 0)
                {
                    _log.Fine(product.GetName() + ", LastInv = " + retValue);
                    return System.Convert.ToDecimal(retValue);
                }
            }
            else	//	VAdvantage.Model.PO second
            {
                //	try Last VAdvantage.Model.PO
                retValue = GetLastPOPrice(product, M_ASI_ID, Org_ID, as1.GetC_Currency_ID());
                if (Org_ID != 0 && (retValue == null || Env.Signum(System.Convert.ToDecimal(retValue)) == 0))
                    retValue = GetLastPOPrice(product, M_ASI_ID, 0, as1.GetC_Currency_ID());
                if (retValue != null && Env.Signum(System.Convert.ToDecimal(retValue)) != 0)
                {
                    _log.Fine(product.GetName() + ", LastPO = " + retValue);
                    return System.Convert.ToDecimal(retValue);
                }
            }

            //	Still nothing try ProductPO
            MProductPO[] pos = MProductPO.GetOfProduct(product.GetCtx(), product.GetM_Product_ID(), null);
            for (int i = 0; i < pos.Length; i++)
            {
                Decimal price = pos[i].GetPricePO();
                if (price == null || Env.Signum(price) == 0)
                    price = pos[0].GetPriceList();
                if (price != null && Env.Signum(price) != 0)
                {
                    price = VAdvantage.Model.MConversionRate.Convert(product.GetCtx(), price,
                        pos[0].GetC_Currency_ID(), as1.GetC_Currency_ID(),
                        as1.GetAD_Client_ID(), Org_ID);
                    if (price != null && Env.Signum(price) != 0)
                    {
                        retValue = price;
                        _log.Fine(product.GetName() + ", Product_PO = " + retValue);
                        return System.Convert.ToDecimal(retValue);
                    }
                }
            }

            //	Still nothing try Purchase Price List
            //	....

            _log.Fine(product.GetName() + " = " + retValue);
            return System.Convert.ToDecimal(retValue);
        }


        /**
         * 	Get Last Invoice Price in currency
         *	@param product product
         *	@param M_ASI_ID attribute set instance
         *	@param AD_Org_ID org
         *	@param C_Currency_ID accounting currency
         *	@return last invoice price in currency
         */
        public static Decimal? GetLastInvoicePrice(MProduct product,
            int M_ASI_ID, int AD_Org_ID, int C_Currency_ID)
        {
            Decimal? retValue = null;
            String sql = "SELECT currencyConvert(il.PriceActual, i.C_Currency_ID," + C_Currency_ID + ", i.DateAcct, i.C_ConversionType_ID, il.AD_Client_ID, il.AD_Org_ID) "
                // ,il.PriceActual, il.QtyInvoiced, i.DateInvoiced, il.Line
                + "FROM C_InvoiceLine il "
                + " INNER JOIN C_Invoice i ON (il.C_Invoice_ID=i.C_Invoice_ID) "
                + "WHERE il.M_Product_ID=" + product.GetM_Product_ID()
                + " AND i.IsSOTrx='N'";
            if (AD_Org_ID != 0)
                sql += " AND il.AD_Org_ID=" + AD_Org_ID;
            else if (M_ASI_ID != 0)
                sql += " AND il.M_AttributeSetInstance_ID=" + M_ASI_ID;
            sql += " ORDER BY i.DateInvoiced DESC, il.Line DESC";
            //
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    retValue = Util.GetValueOfDecimal(dr[0].ToString());
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            if (retValue != null)
            {
                _log.Finer(product.GetName() + " = " + retValue);
                return retValue;
            }
            return null;
        }

        /**
         * 	Get Last VAdvantage.Model.PO Price in currency
         *	@param product product
         *	@param M_ASI_ID attribute set instance
         *	@param AD_Org_ID org
         *	@param C_Currency_ID accounting currency
         *	@return last VAdvantage.Model.PO price in currency or null
         */
        public static Decimal? GetLastPOPrice(MProduct product, int M_ASI_ID, int AD_Org_ID, int C_Currency_ID)
        {
            Decimal? retValue = null;
            String sql = "SELECT currencyConvert(ol.PriceCost, o.C_Currency_ID," + C_Currency_ID + ", o.DateAcct, o.C_ConversionType_ID, ol.AD_Client_ID, ol.AD_Org_ID),"
                + " currencyConvert(ol.PriceActual, o.C_Currency_ID," + C_Currency_ID + ", o.DateAcct, o.C_ConversionType_ID, ol.AD_Client_ID, ol.AD_Org_ID) "
                //	,ol.PriceCost,ol.PriceActual, ol.QtyOrdered, o.DateOrdered, ol.Line
                + "FROM C_OrderLine ol"
                + " INNER JOIN C_Order o ON (ol.C_Order_ID=o.C_Order_ID) "
                + "WHERE ol.M_Product_ID=" + product.GetM_Product_ID()
                + " AND o.IsSOTrx='N'";
            if (AD_Org_ID != 0)
                sql += " AND ol.AD_Org_ID=" + AD_Org_ID;
            else if (M_ASI_ID != 0)
                sql += " AND t.M_AttributeSetInstance_ID=" + M_ASI_ID;
            sql += " ORDER BY o.DateOrdered DESC, ol.Line DESC";
            //
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    retValue = Util.GetValueOfDecimal(dr[0].ToString());
                    if (retValue == null || Env.Signum(System.Convert.ToDecimal(retValue)) == 0)
                    {
                        retValue = Util.GetValueOfDecimal(dr[1].ToString());
                    }
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally
            {
                dt = null;
            }

            if (retValue != null)
            {
                _log.Finer(product.GetName() + " = " + retValue);
                return retValue;
            }
            return null;
        }

        /**
         * 	Get VAdvantage.Model.PO Price in currency
         * 	@param product product
         *	@param C_OrderLine_ID order line
         *	@param C_Currency_ID accounting currency
         *	@return last VAdvantage.Model.PO price in currency or null
         */
        public static Decimal? GetPOPrice(MProduct product, int C_OrderLine_ID, int C_Currency_ID)
        {
            Decimal? retValue = null;
            String sql = "SELECT currencyConvert(ol.PriceCost, o.C_Currency_ID, " + C_Currency_ID + ", o.DateAcct, o.C_ConversionType_ID, ol.AD_Client_ID, ol.AD_Org_ID),"
                + " currencyConvert(ol.PriceActual, o.C_Currency_ID, " + C_Currency_ID + ", o.DateAcct, o.C_ConversionType_ID, ol.AD_Client_ID, ol.AD_Org_ID) "
                //	,ol.PriceCost,ol.PriceActual, ol.QtyOrdered, o.DateOrdered, ol.Line
                + "FROM C_OrderLine ol"
                + " INNER JOIN C_Order o ON (ol.C_Order_ID=o.C_Order_ID) "
                + "WHERE ol.C_OrderLine_ID=" + C_OrderLine_ID
                + " AND o.IsSOTrx='N'";
            //
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    retValue = Util.GetValueOfDecimal(dr[0].ToString());
                    if (retValue == null || Env.Signum(System.Convert.ToDecimal(retValue)) == 0)
                    {
                        retValue = Util.GetValueOfDecimal(dr[1].ToString());
                    }
                }

            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally
            {
                dt = null;
            }

            if (retValue != null)
            {
                _log.Finer(product.GetName() + " = " + retValue);
                return retValue;
            }
            return null;
        }

        /***
         * 	Create costing for client.
         * 	Handles Transaction if not in a transaction
         *	@param client client
         */
        public static void Create(VAdvantage.Model.MClient client)
        {
            VAdvantage.Model.MAcctSchema[] ass = VAdvantage.Model.MAcctSchema.GetClientAcctSchema(client.GetCtx(), client.GetAD_Client_ID());
            Trx trx = client.Get_Trx();
            //String trxNameUsed = trxName;
            if (trx == null)
            {
                //trxNameUsed = Trx.CreateTrxName("Cost");
                trx = Trx.Get("Cost");
            }
            bool success = true;
            //	For all Products
            String sql = "SELECT * FROM M_Product p "
                + "WHERE AD_Client_ID=" + client.GetAD_Client_ID()
                + " AND EXISTS (SELECT * FROM M_CostDetail cd "
                    + "WHERE p.M_Product_ID=cd.M_Product_ID AND Processed='N')";
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, trx);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    MProduct product = new MProduct(client.GetCtx(), dr, trx);
                    for (int i = 0; i < ass.Length; i++)
                    {
                        Decimal cost = GetCurrentCost(product, 0, ass[i], 0,
                            null, Env.ONE, 0, false, trx);		//	create non-zero costs
                        _log.Info(product.GetName() + " = " + cost);
                    }
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
                success = false;
            }
            finally { dt = null; }

            //	Transaction
            if (trx != null)
            {
                if (success)
                {
                    trx.Commit();
                }
                else
                {
                    trx.Rollback();
                }
                trx.Close();
            }
        }

        /**
         * 	Create standard Costing records for Product
         *	@param product product
         */
        public static void Create(MProduct product)
        {
            _log.Config(product.GetName());
            //	Cost Elements
            VAdvantage.Model.MCostElement[] ces = VAdvantage.Model.MCostElement.GetCostingMethods(product);
            VAdvantage.Model.MCostElement ce = null;
            for (int i = 0; i < ces.Length; i++)
            {
                if (VAdvantage.Model.MCostElement.COSTINGMETHOD_StandardCosting.Equals(ces[i].GetCostingMethod()))
                {
                    ce = ces[i];
                    break;
                }
            }
            if (ce == null)
            {
                _log.Fine("No Standard Costing in System");
                return;
            }

            VAdvantage.Model.MAcctSchema[] mass = VAdvantage.Model.MAcctSchema.GetClientAcctSchema(product.GetCtx(),
                product.GetAD_Client_ID(), product.Get_TrxName());
            VAdvantage.Model.MOrg[] orgs = null;

            int M_ASI_ID = 0;		//	No Attribute
            for (int i = 0; i < mass.Length; i++)
            {
                VAdvantage.Model.MAcctSchema as1 = mass[i];
                MProductCategoryAcct pca = MProductCategoryAcct.Get(product.GetCtx(),
                    product.GetM_Product_Category_ID(), as1.GetC_AcctSchema_ID(), product.Get_TrxName());
                String cl = null;
                if (pca == null)
                    cl = pca.GetCostingLevel();
                if (cl == null)
                    cl = as1.GetCostingLevel();
                //	Create Std Costing
                if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_Client.Equals(cl))
                {
                    MCost cost = MCost.Get(product, M_ASI_ID,
                        as1, 0, ce.GetM_CostElement_ID());
                    if (cost.Is_New())
                    {
                        if (cost.Save())
                        {
                            _log.Config("Std.Cost for " + product.GetName() + " - " + as1.GetName());
                        }
                        else
                        {
                            _log.Warning("Not created: Std.Cost for " + product.GetName() + " - " + as1.GetName());
                        }
                    }
                }
                else if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_Organization.Equals(cl))
                {
                    if (orgs == null)
                        orgs = VAdvantage.Model.MOrg.GetOfClient(product);
                    for (int o = 0; o < orgs.Length; o++)
                    {
                        MCost cost = MCost.Get(product, M_ASI_ID,
                            as1, orgs[o].GetAD_Org_ID(), ce.GetM_CostElement_ID());
                        if (cost.Is_New())
                        {
                            if (cost.Save())
                            {
                                _log.Config("Std.Cost for " + product.GetName()
                                   + " - " + orgs[o].GetName()
                                   + " - " + as1.GetName());
                            }
                            else
                            {
                                _log.Warning("Not created: Std.Cost for " + product.GetName()
                                    + " - " + orgs[o].GetName()
                                    + " - " + as1.GetName());
                            }
                        }
                    }	//	for all orgs
                }
                else
                {
                    _log.Warning("Not created: Std.Cost for " + product.GetName() + " - Costing Level on Batch/Lot");
                }
            }	//	accounting schema loop

        }

        /**
         * 	Calculate Average Invoice from Trx
         *	@param product product
         *	@param M_AttributeSetInstance_ID optional asi
         *	@param as1 acct schema
         *	@param AD_Org_ID optonal org
         *	@return average costs or null
         */
        public static Decimal? CalculateAverageInv(MProduct product, int M_AttributeSetInstance_ID, VAdvantage.Model.MAcctSchema as1, int AD_Org_ID)
        {
            String sql = "SELECT t.MovementQty, mi.Qty, il.QtyInvoiced, il.PriceActual,"
                + " i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, t.M_Transaction_ID "
                + "FROM M_Transaction t"
                + " INNER JOIN M_MatchInv mi ON (t.M_InOutLine_ID=mi.M_InOutLine_ID)"
                + " INNER JOIN C_InvoiceLine il ON (mi.C_InvoiceLine_ID=il.C_InvoiceLine_ID)"
                + " INNER JOIN C_Invoice i ON (il.C_Invoice_ID=i.C_Invoice_ID) "
                + "WHERE t.M_Product_ID=" + product.GetM_Product_ID();
            if (AD_Org_ID != 0)
                sql += " AND t.AD_Org_ID=" + AD_Org_ID;
            else if (M_AttributeSetInstance_ID != 0)
                sql += " AND t.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID;
            sql += " ORDER BY t.M_Transaction_ID";

            DataTable dt = null;
            Decimal newStockQty = Env.ZERO;
            //
            Decimal newAverageAmt = Env.ZERO;
            int oldTransaction_ID = 0;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, null);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    Decimal oldStockQty = newStockQty;
                    Decimal movementQty = Util.GetValueOfDecimal(dr[0].ToString());
                    int M_Transaction_ID = Util.GetValueOfInt(dr[9].ToString());//.getInt(10);
                    if (M_Transaction_ID != oldTransaction_ID)
                    {
                        newStockQty = Decimal.Add(oldStockQty, movementQty);
                    }
                    M_Transaction_ID = oldTransaction_ID;
                    //
                    Decimal matchQty = Util.GetValueOfDecimal(dr[1].ToString());
                    if (matchQty == null)
                    {
                        _log.Finer("Movement=" + movementQty + ", StockQty=" + newStockQty);
                        continue;
                    }
                    //	Assumption: everything is matched
                    Decimal price = Util.GetValueOfDecimal(dr[3].ToString());
                    int C_Currency_ID = Util.GetValueOfInt(dr[4].ToString());
                    DateTime? DateAcct = Util.GetValueOfDateTime(dr[5]);
                    int C_ConversionType_ID = Util.GetValueOfInt(dr[6].ToString());
                    int Client_ID = Util.GetValueOfInt(dr[7].ToString());
                    int Org_ID = Util.GetValueOfInt(dr[8].ToString());
                    Decimal cost = VAdvantage.Model.MConversionRate.Convert(product.GetCtx(), price,
                        C_Currency_ID, as1.GetC_Currency_ID(),
                        DateAcct, C_ConversionType_ID, Client_ID, Org_ID);
                    //
                    Decimal oldAverageAmt = newAverageAmt;
                    Decimal averageCurrent = Decimal.Multiply(oldStockQty, oldAverageAmt);
                    Decimal averageIncrease = Decimal.Multiply(matchQty, cost);
                    Decimal newAmt = Decimal.Add(averageCurrent, averageIncrease);
                    newAmt = Decimal.Round(newAmt, as1.GetCostingPrecision());
                    newAverageAmt = Decimal.Divide(newAmt, Decimal.Round(newStockQty, as1.GetCostingPrecision(), MidpointRounding.AwayFromZero));
                    _log.Finer("Movement=" + movementQty + ", StockQty=" + newStockQty
                       + ", Match=" + matchQty + ", Cost=" + cost + ", NewAvg=" + newAverageAmt);
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            //
            if (Env.Signum(newAverageAmt) != 0)
            {
                _log.Finer(product.GetName() + " = " + newAverageAmt);
                return newAverageAmt;
            }
            return null;
        }

        /**
         * 	Calculate Average VAdvantage.Model.PO
         *	@param product product
         *	@param M_AttributeSetInstance_ID asi
         *	@param as1 acct schema
         *	@param AD_Org_ID org
         *	@return costs or null
         */
        public static Decimal? CalculateAveragePO(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID)
        {
            String sql = "SELECT t.MovementQty, mp.Qty, ol.QtyOrdered, ol.PriceCost, ol.PriceActual,"	//	1..5
                + " o.C_Currency_ID, o.DateAcct, o.C_ConversionType_ID,"	//	6..8
                + " o.AD_Client_ID, o.AD_Org_ID, t.M_Transaction_ID "		//	9..11
                + "FROM M_Transaction t"
                + " INNER JOIN M_MatchPO mp ON (t.M_InOutLine_ID=mp.M_InOutLine_ID)"
                + " INNER JOIN C_OrderLine ol ON (mp.C_OrderLine_ID=ol.C_OrderLine_ID)"
                + " INNER JOIN C_Order o ON (ol.C_Order_ID=o.C_Order_ID) "
                + "WHERE t.M_Product_ID=" + product.GetM_Product_ID();
            if (AD_Org_ID != 0)
                sql += " AND t.AD_Org_ID=" + AD_Org_ID;
            else if (M_AttributeSetInstance_ID != 0)
                sql += " AND t.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID;
            sql += " ORDER BY t.M_Transaction_ID";

            DataTable dt = null;
            Decimal newStockQty = Env.ZERO;
            //
            Decimal newAverageAmt = Env.ZERO;
            int oldTransaction_ID = 0;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, null);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    Decimal oldStockQty = newStockQty;
                    Decimal movementQty = Util.GetValueOfDecimal(dr[0].ToString());
                    int M_Transaction_ID = Util.GetValueOfInt(dr[10].ToString());
                    if (M_Transaction_ID != oldTransaction_ID)
                    {
                        newStockQty = Decimal.Add(oldStockQty, movementQty);
                    }
                    M_Transaction_ID = oldTransaction_ID;
                    //
                    Decimal matchQty = Util.GetValueOfDecimal(dr[1].ToString());
                    if (matchQty == null)
                    {
                        _log.Finer("Movement=" + movementQty + ", StockQty=" + newStockQty);
                        continue;
                    }
                    //	Assumption: everything is matched
                    Decimal price = Util.GetValueOfDecimal(dr[3].ToString());
                    if (price == null || Env.Signum(price) == 0)	//	VAdvantage.Model.PO Cost
                    {
                        price = Util.GetValueOfDecimal(dr[4].ToString());
                    }
                    int C_Currency_ID = Util.GetValueOfInt(dr[5].ToString());
                    DateTime? DateAcct = Util.GetValueOfDateTime(dr[6]);
                    int C_ConversionType_ID = Util.GetValueOfInt(dr[7].ToString());
                    int Client_ID = Util.GetValueOfInt(dr[8].ToString());
                    int Org_ID = Util.GetValueOfInt(dr[9].ToString());
                    Decimal cost = VAdvantage.Model.MConversionRate.Convert(product.GetCtx(), price,
                        C_Currency_ID, as1.GetC_Currency_ID(),
                        DateAcct, C_ConversionType_ID, Client_ID, Org_ID);
                    //
                    Decimal oldAverageAmt = newAverageAmt;
                    Decimal averageCurrent = Decimal.Multiply(oldStockQty, oldAverageAmt);
                    Decimal averageIncrease = Decimal.Multiply(matchQty, cost);
                    Decimal newAmt = Decimal.Add(averageCurrent, averageIncrease);
                    newAmt = Decimal.Round(newAmt, (as1.GetCostingPrecision()));
                    newAverageAmt = Decimal.Divide(newAmt, Decimal.Round(newStockQty, as1.GetCostingPrecision(), MidpointRounding.AwayFromZero));
                    _log.Finer("Movement=" + movementQty + ", StockQty=" + newStockQty
                        + ", Match=" + matchQty + ", Cost=" + cost + ", NewAvg=" + newAverageAmt);
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            if (newAverageAmt != null & Env.Signum(newAverageAmt) != 0)
            {
                _log.Finer(product.GetName() + " = " + newAverageAmt);
                return newAverageAmt;
            }
            return null;
        }

        /**
         * 	Calculate FiFo Cost
         *	@param product product
         *	@param M_AttributeSetInstance_ID asi
         *	@param as1 acct schema
         *	@param AD_Org_ID org
         *	@return costs or null
         */
        public static Decimal? CalculateFiFo(MProduct product, int M_AttributeSetInstance_ID, VAdvantage.Model.MAcctSchema as1, int AD_Org_ID)
        {
            String sql = "SELECT t.MovementQty, mi.Qty, il.QtyInvoiced, il.PriceActual,"
                + " i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, t.M_Transaction_ID "
                + "FROM M_Transaction t"
                + " INNER JOIN M_MatchInv mi ON (t.M_InOutLine_ID=mi.M_InOutLine_ID)"
                + " INNER JOIN C_InvoiceLine il ON (mi.C_InvoiceLine_ID=il.C_InvoiceLine_ID)"
                + " INNER JOIN C_Invoice i ON (il.C_Invoice_ID=i.C_Invoice_ID) "
                + "WHERE t.M_Product_ID=" + product.GetM_Product_ID();
            if (AD_Org_ID != 0)
                sql += " AND t.AD_Org_ID=" + AD_Org_ID;
            else if (M_AttributeSetInstance_ID != 0)
                sql += " AND t.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID;
            sql += " ORDER BY t.M_Transaction_ID";

            DataTable dt = null;
            //
            int oldTransaction_ID = 0;
            List<QtyCost> fifo = new List<QtyCost>();
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, null);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    Decimal movementQty = Util.GetValueOfDecimal(dr[0].ToString());
                    int M_Transaction_ID = Util.GetValueOfInt(dr[9].ToString());
                    if (M_Transaction_ID == oldTransaction_ID)
                    {
                        continue;	//	assuming same price for receipt
                    }
                    M_Transaction_ID = oldTransaction_ID;
                    //
                    Decimal matchQty = Util.GetValueOfDecimal(dr[1].ToString());
                    if (matchQty == null)	//	out (negative)
                    {
                        if (fifo.Count > 0)
                        {
                            QtyCost pp = (QtyCost)fifo[0];
                            pp.Qty = Decimal.Add((Decimal)pp.Qty, movementQty);
                            Decimal remainder = (Decimal)pp.Qty;
                            if (Env.Signum(remainder) == 0)
                            {
                                fifo.RemoveAt(0);
                            }
                            else
                            {
                                while (Env.Signum(remainder) != 0)
                                {
                                    if (fifo.Count == 1)	//	Last
                                    {
                                        pp.Cost = Env.ZERO;
                                        remainder = Env.ZERO;
                                    }
                                    else
                                    {
                                        fifo.RemoveAt(0);
                                        pp = (QtyCost)fifo[0];
                                        pp.Qty = Decimal.Add((Decimal)pp.Qty, movementQty);
                                        remainder = (Decimal)pp.Qty;
                                    }
                                }
                            }
                        }
                        else
                        {
                            QtyCost pp = new QtyCost(movementQty, Env.ZERO);
                            fifo.Add(pp);
                        }
                        _log.Finer("Movement=" + movementQty + ", Size=" + fifo.Count);
                        continue;
                    }
                    //	Assumption: everything is matched
                    Decimal price = Util.GetValueOfDecimal(dr[3].ToString());
                    int C_Currency_ID = Util.GetValueOfInt(dr[4].ToString());
                    DateTime? DateAcct = Util.GetValueOfDateTime(dr[5]);
                    int C_ConversionType_ID = Util.GetValueOfInt(dr[6].ToString());
                    int Client_ID = Util.GetValueOfInt(dr[7].ToString());
                    int Org_ID = Util.GetValueOfInt(dr[8].ToString());
                    Decimal cost = VAdvantage.Model.MConversionRate.Convert(product.GetCtx(), price,
                        C_Currency_ID, as1.GetC_Currency_ID(),
                        DateAcct, C_ConversionType_ID, Client_ID, Org_ID);

                    //	Add Stock
                    bool used = false;
                    if (fifo.Count == 1)
                    {
                        QtyCost pp = (QtyCost)fifo[0];
                        //if (pp.Qty.signum() < 0)
                        if (Env.Signum(System.Convert.ToDecimal(pp.Qty)) < 0)
                        {
                            pp.Qty = Decimal.Add(System.Convert.ToDecimal(pp.Qty), movementQty);
                            if (Env.Signum(System.Convert.ToDecimal(pp.Qty)) == 0)
                            {
                                fifo.RemoveAt(0);
                            }
                            else
                            {
                                pp.Cost = cost;
                            }
                            used = true;
                        }
                    }
                    if (!used)
                    {
                        QtyCost pp = new QtyCost(movementQty, cost);
                        fifo.Add(pp);
                    }
                    _log.Finer("Movement=" + movementQty + ", Size=" + fifo.Count);
                }

            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }

            if (fifo.Count == 0)
                return null;
            QtyCost pp1 = (QtyCost)fifo[0];
            _log.Finer(product.GetName() + " = " + pp1.Cost);
            return pp1.Cost;
        }

        /**
         * 	Calculate LiFo costs
         *	@param product product
         *	@param M_AttributeSetInstance_ID asi
         *	@param as1 acct schema
         *	@param AD_Org_ID org
         *	@return costs or null
         */
        public static Decimal? CalculateLiFo(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID)
        {
            String sql = "SELECT t.MovementQty, mi.Qty, il.QtyInvoiced, il.PriceActual,"
                + " i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, t.M_Transaction_ID "
                + "FROM M_Transaction t"
                + " INNER JOIN M_MatchInv mi ON (t.M_InOutLine_ID=mi.M_InOutLine_ID)"
                + " INNER JOIN C_InvoiceLine il ON (mi.C_InvoiceLine_ID=il.C_InvoiceLine_ID)"
                + " INNER JOIN C_Invoice i ON (il.C_Invoice_ID=i.C_Invoice_ID) "
                + "WHERE t.M_Product_ID=" + product.GetM_Product_ID();
            if (AD_Org_ID != 0)
                sql += " AND t.AD_Org_ID=" + AD_Org_ID;
            else if (M_AttributeSetInstance_ID != 0)
                sql += " AND t.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID;
            //	Starting point?
            sql += " ORDER BY t.M_Transaction_ID DESC";

            DataTable dt = null;
            //
            int oldTransaction_ID = 0;
            List<QtyCost> lifo = new List<QtyCost>();
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, null);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    Decimal movementQty = Util.GetValueOfDecimal(dr[0].ToString());
                    int M_Transaction_ID = Util.GetValueOfInt(dr[9].ToString());
                    if (M_Transaction_ID == oldTransaction_ID)
                    {
                        continue;	//	assuming same price for receipt
                    }
                    M_Transaction_ID = oldTransaction_ID;
                    //
                    Decimal matchQty = Util.GetValueOfDecimal(dr[1].ToString());
                    if (matchQty == null)	//	out (negative)
                    {
                        if (lifo.Count > 0)
                        {
                            QtyCost pp = (QtyCost)lifo[lifo.Count - 1];
                            pp.Qty = Decimal.Add((Decimal)pp.Qty, movementQty);
                            Decimal remainder = (Decimal)pp.Qty;
                            if (Env.Signum(remainder) == 0)
                            {
                                lifo.RemoveAt(lifo.Count - 1);
                            }
                            else
                            {
                                while (Env.Signum(remainder) != 0)
                                {
                                    if (lifo.Count == 1)	//	Last
                                    {
                                        pp.Cost = Env.ZERO;
                                        remainder = Env.ZERO;
                                    }
                                    else
                                    {
                                        lifo.RemoveAt(lifo.Count - 1);
                                        pp = (QtyCost)lifo[lifo.Count - 1];
                                        pp.Qty = Decimal.Add((Decimal)pp.Qty, movementQty);
                                        remainder = (Decimal)pp.Qty;
                                    }
                                }
                            }
                        }
                        else
                        {
                            QtyCost pp = new QtyCost(movementQty, Env.ZERO);
                            lifo.Add(pp);
                        }
                        _log.Finer("Movement=" + movementQty + ", Size=" + lifo.Count);
                        continue;
                    }
                    //	Assumption: everything is matched
                    Decimal price = Util.GetValueOfDecimal(dr[3].ToString());
                    int C_Currency_ID = Util.GetValueOfInt(dr[4].ToString());
                    DateTime? DateAcct = Util.GetValueOfDateTime(dr[5]);
                    int C_ConversionType_ID = Util.GetValueOfInt(dr[6].ToString());
                    int Client_ID = Util.GetValueOfInt(dr[7].ToString());
                    int Org_ID = Util.GetValueOfInt(dr[8].ToString());
                    Decimal cost = VAdvantage.Model.MConversionRate.Convert(product.GetCtx(), price,
                        C_Currency_ID, as1.GetC_Currency_ID(),
                        DateAcct, C_ConversionType_ID, Client_ID, Org_ID);
                    //
                    QtyCost pp1 = new QtyCost(movementQty, cost);
                    lifo.Add(pp1);
                    _log.Finer("Movement=" + movementQty + ", Size=" + lifo.Count);
                }

            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            if (lifo.Count == 0)
            {
                return null;
            }
            QtyCost pp2 = (QtyCost)lifo[lifo.Count - 1];
            _log.Finer(product.GetName() + " = " + pp2.Cost);
            return pp2.Cost;
        }


        /**
         *	MCost Qty-Cost Pair
         */
        /****************************************************************************************
        In java instance of static class is creted not in .net also .net no parametrised constructor used
        /****************************************************************************************/
        //static class QtyCost
        public class QtyCost
        {
            //Qty		
            public Decimal? Qty = null;
            // Cost	
            public Decimal? Cost = null;

            /**
             * 	Constructor
             *	@param qty qty
             *	@param cost cost
             */
            public QtyCost(Decimal qty, Decimal cost)
            {
                Qty = qty;
                Cost = cost;
            }


            /**
             * 	String Representation
             *	@return info
             */
            public override String ToString()
            {
                StringBuilder sb = new StringBuilder("Qty=").Append(Qty)
                    .Append(",Cost=").Append(Cost);
                return sb.ToString();
            }
        }


        /**
         * 	Get/Create Cost Record.
         * 	CostingLevel is not validated
         *	@param product product
         *	@param M_AttributeSetInstance_ID costing level asi
         *	@param as1 accounting schema
         *	@param AD_Org_ID costing level org
         *	@param M_CostElement_ID element
         *	@return cost price or null
         */

        /* Addes By Bharat 08/July/2014 */
        public static MCost Get(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID, int M_CostElement_ID, int A_Asset_ID)
        {
            MCost cost = null;
            String sql = "SELECT * "
                + "FROM M_Cost c "
                + "WHERE AD_Client_ID=" + product.GetAD_Client_ID() + " AND AD_Org_ID=" + AD_Org_ID
                + " AND M_Product_ID=" + product.GetM_Product_ID()
                + " AND M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID
                + " AND M_CostType_ID=" + as1.GetM_CostType_ID() + " AND C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID()
                + " AND M_CostElement_ID=" + M_CostElement_ID + "AND A_Asset_ID=" + A_Asset_ID + "AND ISAssetCost= 'Y'";
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    cost = new MCost(product.GetCtx(), dr, product.Get_TrxName());
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            //	New
            if (cost == null)
                cost = new MCost(product, M_AttributeSetInstance_ID,
                    as1, AD_Org_ID, M_CostElement_ID, A_Asset_ID);
            return cost;
        }
        /*  Addes By Bharat 08/July/2014   */

        public static MCost Get(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID, int M_CostElement_ID)
        {
            MCost cost = null;
            String sql = "SELECT * "
                + "FROM M_Cost c "
                + "WHERE AD_Client_ID=" + product.GetAD_Client_ID() + " AND AD_Org_ID=" + AD_Org_ID
                + " AND M_Product_ID=" + product.GetM_Product_ID()
                + " AND M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID
                + " AND M_CostType_ID=" + as1.GetM_CostType_ID() + " AND C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID()
                + " AND M_CostElement_ID=" + M_CostElement_ID + " AND ISAssetCost= 'N'";
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    cost = new MCost(product.GetCtx(), dr, product.Get_TrxName());
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            //	New
            if (cost == null)
                cost = new MCost(product, M_AttributeSetInstance_ID,
                    as1, AD_Org_ID, M_CostElement_ID);
            return cost;
        }

        public static MCost[] Get(int M_AttributeSetInstance_ID,
        VAdvantage.Model.MAcctSchema as1, int M_CostType_ID, int AD_Org_ID, MProduct product)
        {
            String CostingLevel = as1.GetCostingLevel();
            MProductCategoryAcct pca = MProductCategoryAcct.Get(product.GetCtx(),
                    product.GetM_Product_Category_ID(), as1.GetC_AcctSchema_ID(), null);

            if (pca == null)
            {
                throw new Exception("Cannot find Acct for M_Product_Category_ID="
                        + product.GetM_Product_Category_ID()
                        + ", C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID());
            }
            //	Costing Level
            if (pca.GetCostingLevel() != null)
                CostingLevel = pca.GetCostingLevel();
            if (X_C_AcctSchema.COSTINGLEVEL_Client.Equals(CostingLevel))
            {
                AD_Org_ID = 0;
                M_AttributeSetInstance_ID = 0;
            }
            else if (X_C_AcctSchema.COSTINGLEVEL_Organization.Equals(CostingLevel))
                M_AttributeSetInstance_ID = 0;
            else if (X_C_AcctSchema.COSTINGLEVEL_BatchLot.Equals(CostingLevel))
                AD_Org_ID = 0;
            //	Costing Method is standard only right now. Will have to change this once others are included.

            //	TODO Create/Update Costs Do we need this

            MCost cost = null;
            List<MCost> list = new List<MCost>();
            String sql = "SELECT c.* "
                + "FROM M_Cost c "
                + " LEFT OUTER JOIN M_CostElement ce ON (c.M_CostElement_ID=ce.M_CostElement_ID) "
                + "WHERE c.AD_Client_ID=" + product.GetAD_Client_ID() + " AND c.AD_Org_ID=" + AD_Org_ID
                + " AND c.M_Product_ID=" + product.GetM_Product_ID()
                + " AND (c.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID + " OR c.M_AttributeSetInstance_ID=0) ";
            if (M_CostType_ID == 0)
            {
                //pstmt.setInt (5, as1.GetM_CostType_ID());
                sql += " AND c.M_CostType_ID=" + as1.GetM_CostType_ID();
            }
            else
            {
                //pstmt.setInt (5, M_CostType_ID);
                sql += " AND c.M_CostType_ID= " + M_CostType_ID;
            }

            sql += " AND (ce.CostingMethod IS NULL OR ce.CostingMethod='" + X_M_CostElement.COSTINGMETHOD_StandardCosting + "') "
            + " AND c.IsActive = 'Y' ";
            if (as1 != null)
            {
                sql = sql + " AND C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID();
            }

            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                DataTable dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    cost = new MCost(product.GetCtx(), dr, null);
                    list.Add(cost);
                }
            }
            catch (Exception e)
            {
                _log.Log(Level.SEVERE, sql, e);
            }
            finally
            {
                if (idr != null)
                {
                    idr.Close();
                    idr = null;
                }
            }
            MCost[] costs = new MCost[list.Count];
            costs = list.ToArray();
            return costs;
        }

        public static MCost[] Get(int M_AttributeSetInstance_ID,
       VAdvantage.Model.MAcctSchema as1, int M_CostType_ID, int AD_Org_ID, int productID)
        {
            MProduct product = new MProduct(Env.GetCtx(), productID, null);
            String CostingLevel = as1.GetCostingLevel();
            MProductCategoryAcct pca = MProductCategoryAcct.Get(product.GetCtx(),
                    product.GetM_Product_Category_ID(), as1.GetC_AcctSchema_ID(), null);

            if (pca == null)
            {
                throw new Exception("Cannot find Acct for M_Product_Category_ID="
                        + product.GetM_Product_Category_ID()
                        + ", C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID());
            }
            //	Costing Level
            if (pca.GetCostingLevel() != null)
                CostingLevel = pca.GetCostingLevel();
            if (X_C_AcctSchema.COSTINGLEVEL_Client.Equals(CostingLevel))
            {
                AD_Org_ID = 0;
                M_AttributeSetInstance_ID = 0;
            }
            else if (X_C_AcctSchema.COSTINGLEVEL_Organization.Equals(CostingLevel))
                M_AttributeSetInstance_ID = 0;
            else if (X_C_AcctSchema.COSTINGLEVEL_BatchLot.Equals(CostingLevel))
                AD_Org_ID = 0;
            //	Costing Method is standard only right now. Will have to change this once others are included.

            //	TODO Create/Update Costs Do we need this

            MCost cost = null;
            List<MCost> list = new List<MCost>();
            String sql = "SELECT c.* "
                + "FROM M_Cost c "
                + " LEFT OUTER JOIN M_CostElement ce ON (c.M_CostElement_ID=ce.M_CostElement_ID) "
                + "WHERE c.AD_Client_ID=" + product.GetAD_Client_ID() + " AND c.AD_Org_ID=" + AD_Org_ID
                + " AND c.M_Product_ID=" + product.GetM_Product_ID()
                + " AND (c.M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID + " OR c.M_AttributeSetInstance_ID=0) ";
            if (M_CostType_ID == 0)
            {
                //pstmt.setInt (5, as1.GetM_CostType_ID());
                sql += " AND c.M_CostType_ID=" + as1.GetM_CostType_ID();
            }
            else
            {
                //pstmt.setInt (5, M_CostType_ID);
                sql += " AND c.M_CostType_ID= " + M_CostType_ID;
            }

            sql += " AND (ce.CostingMethod IS NULL OR ce.CostingMethod='" + X_M_CostElement.COSTINGMETHOD_StandardCosting + "') "
            + " AND c.IsActive = 'Y' ";
            if (as1 != null)
            {
                sql = sql + " AND C_AcctSchema_ID=" + as1.GetC_AcctSchema_ID();
            }

            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, product.Get_TrxName());
                DataTable dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    cost = new MCost(product.GetCtx(), dr, null);
                    list.Add(cost);
                }
            }
            catch (Exception e)
            {
                _log.Log(Level.SEVERE, sql, e);
            }
            finally
            {
                if (idr != null)
                {
                    idr.Close();
                    idr = null;
                }
            }
            MCost[] costs = new MCost[list.Count];
            costs = list.ToArray();
            return costs;
        }


        /**
         * 	Get Costs
         * 	@param ctx context
         *	@param AD_Client_ID client
         *	@param AD_Org_ID org
         *	@param M_Product_ID product
         *	@param M_CostType_ID cost type
         *	@param C_AcctSchema_ID as1
         *	@param M_CostElement_ID cost element
         *	@param M_AttributeSetInstance_ID asi
         *	@return cost or null
         */
        public static MCost Get(Ctx ctx, int AD_Client_ID, int AD_Org_ID, int M_Product_ID,
            int M_CostType_ID, int C_AcctSchema_ID, int M_CostElement_ID,
            int M_AttributeSetInstance_ID)
        {
            MCost retValue = null;
            String sql = "SELECT * FROM M_Cost "
                + "WHERE AD_Client_ID=" + AD_Client_ID + " AND AD_Org_ID=" + AD_Org_ID + " AND M_Product_ID=" + M_Product_ID
                + " AND M_CostType_ID=" + M_CostType_ID + " AND C_AcctSchema_ID=" + C_AcctSchema_ID + " AND M_CostElement_ID=" + M_CostElement_ID
                + " AND M_AttributeSetInstance_ID=" + M_AttributeSetInstance_ID;
            DataTable dt = null;
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql, null, null);
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)
                {
                    retValue = new MCost(ctx, dr, null);
                }
            }
            catch (Exception e)
            {
                if (idr != null)
                {
                    idr.Close();
                }
                _log.Log(Level.SEVERE, sql, e);
            }
            finally { dt = null; }
            return retValue;
        }

        /**
         * 	Standard Constructor
         *	@param ctx context
         *	@param ignored multi-key
         *	@param trxName trx
         */
        public MCost(Ctx ctx, int ignored, Trx trxName)
            : base(ctx, ignored, trxName)
        {

            if (ignored == 0)
            {
                //	setC_AcctSchema_ID (0);
                //	setM_CostElement_ID (0);
                //	setM_CostType_ID (0);
                //	setM_Product_ID (0);
                SetM_AttributeSetInstance_ID(0);
                //
                SetCurrentCostPrice(Env.ZERO);
                SetFutureCostPrice(Env.ZERO);
                SetCurrentQty(Env.ZERO);
                SetCumulatedAmt(Env.ZERO);
                SetCumulatedQty(Env.ZERO);
            }
            else
                throw new ArgumentException("Multi-Key");
        }

        /**
         * 	Load Constructor
         *	@param ctx context
         *	@param dr result set
         *	@param trxName trx
         */
        public MCost(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {
            _manual = false;
        }


        /**                      Added By Bharat 10-July-2014 
         * 	Parent Constructor
         *	@param product Product
         *	@param M_AttributeSetInstance_ID asi
         *	@param as1 Acct Schema
         *	@param AD_Org_ID org
         *	@param M_CostElement_ID cost element
         */
        public MCost(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID, int M_CostElement_ID, int A_Asset_ID)
            : this(product.GetCtx(), 0, product.Get_TrxName())
        {
            SetClientOrg(product.GetAD_Client_ID(), AD_Org_ID);
            SetC_AcctSchema_ID(as1.GetC_AcctSchema_ID());
            SetM_CostType_ID(as1.GetM_CostType_ID());
            SetM_Product_ID(product.GetM_Product_ID());
            SetM_AttributeSetInstance_ID(M_AttributeSetInstance_ID);
            SetM_CostElement_ID(M_CostElement_ID);
            if (A_Asset_ID > 0)
                SetA_Asset_ID(A_Asset_ID);
            //
            _manual = false;
        }




        /**
         * 	Parent Constructor
         *	@param product Product
         *	@param M_AttributeSetInstance_ID asi
         *	@param as1 Acct Schema
         *	@param AD_Org_ID org
         *	@param M_CostElement_ID cost element
         */
        public MCost(MProduct product, int M_AttributeSetInstance_ID,
            VAdvantage.Model.MAcctSchema as1, int AD_Org_ID, int M_CostElement_ID)
            : this(product.GetCtx(), 0, product.Get_TrxName())
        {
            SetClientOrg(product.GetAD_Client_ID(), AD_Org_ID);
            SetC_AcctSchema_ID(as1.GetC_AcctSchema_ID());
            SetM_CostType_ID(as1.GetM_CostType_ID());
            SetM_Product_ID(product.GetM_Product_ID());
            SetM_AttributeSetInstance_ID(M_AttributeSetInstance_ID);
            SetM_CostElement_ID(M_CostElement_ID);
            //
            _manual = false;
        }

        /**
         * 	Add Cumulative Amt/Qty and Current Qty
         *	@param amt amt
         *	@param qty qty
         */
        public void Add(Decimal amt, Decimal qty)
        {
            SetCumulatedAmt(Decimal.Add(GetCumulatedAmt(), amt));
            SetCumulatedQty(Decimal.Add(GetCumulatedQty(), qty));
            SetCurrentQty(Decimal.Add(GetCurrentQty(), qty));
        }	//	Add

        /**
         * 	Add Amt/Qty and calculate weighted average.
         * 	((OldAvg*OldQty)+(Price*Qty)) / (OldQty+Qty)
         *	@param amt total amt (price * qty)
         *	@param qty qty
         */
        public void SetWeightedAverage(Decimal amt, Decimal qty)
        {
            Decimal oldSum = Decimal.Multiply(GetCurrentCostPrice(), GetCurrentQty());
            Decimal newSum = amt;	//	is total already
            Decimal sumAmt = Decimal.Add(oldSum, newSum);
            Decimal sumQty = Decimal.Add(GetCurrentQty(), qty);
            if (Env.Signum(sumQty) != 0)
            {
                Decimal cost = Decimal.Round(Decimal.Divide(sumAmt, sumQty), GetPrecision(), MidpointRounding.AwayFromZero);
                SetCurrentCostPrice(cost);
            }
            //
            SetCumulatedAmt(Decimal.Add(GetCumulatedAmt(), amt));
            SetCumulatedQty(Decimal.Add(GetCumulatedQty(), qty));
            SetCurrentQty(Decimal.Add(GetCurrentQty(), qty));
        }

        /**
         * 	Get Costing Precision
         *	@return precision (6)
         */
        private int GetPrecision()
        {
            VAdvantage.Model.MAcctSchema as1 = VAdvantage.Model.MAcctSchema.Get(GetCtx(), GetC_AcctSchema_ID());
            if (as1 != null)
                return as1.GetCostingPrecision();
            return 6;
        }

        /**
         * 	Set Current Cost Price
         *	@param currentCostPrice if null set to 0
         */
        public new void SetCurrentCostPrice(Decimal? currentCostPrice)
        {
            if (currentCostPrice != null)
            {
                //   base.SetCurrentCostPrice((Decimal)Convert.ToDecimal(currentCostPrice));
                base.SetCurrentCostPrice(Decimal.Round(currentCostPrice.Value, GetPrecision(), MidpointRounding.AwayFromZero));
            }
            else
            {
                base.SetCurrentCostPrice(Env.ZERO);
            }
        }

        /**
         * 	Get History Average (Amt/Qty)
         *	@return average if amt/aty <> 0 otherwise null
         */
        public Decimal? GetHistoryAverage()
        {
            Decimal? retValue = null;
            if (Env.Signum(GetCumulatedQty()) != 0
                && Env.Signum(GetCumulatedAmt()) != 0)
                retValue = Decimal.Divide(GetCumulatedAmt(), Decimal.Round(GetCumulatedQty(), GetPrecision(), MidpointRounding.AwayFromZero));
            return retValue;
        }

        /**
         * 	String Representation
         *	@return info
         */
        public override String ToString()
        {
            StringBuilder sb = new StringBuilder("MCost[");
            sb.Append("AD_Client_ID=").Append(GetAD_Client_ID());
            if (GetAD_Org_ID() != 0)
                sb.Append(",AD_Org_ID=").Append(GetAD_Org_ID());
            sb.Append(",M_Product_ID=").Append(GetM_Product_ID());
            if (GetM_AttributeSetInstance_ID() != 0)
                sb.Append(",AD_ASI_ID=").Append(GetM_AttributeSetInstance_ID());
            //	sb.Append (",C_AcctSchema_ID=").Append (getC_AcctSchema_ID());
            //	sb.Append (",M_CostType_ID=").Append (getM_CostType_ID());
            sb.Append(",M_CostElement_ID=").Append(GetM_CostElement_ID());
            //
            sb.Append(", CurrentCost=").Append(GetCurrentCostPrice())
                .Append(", C.Amt=").Append(GetCumulatedAmt())
                .Append(",C.Qty=").Append(GetCumulatedQty())
                .Append("]");
            return sb.ToString();
        }

        /**
         * 	Get Cost Element
         *	@return cost element
         */
        public VAdvantage.Model.MCostElement GetCostElement()
        {
            int M_CostElement_ID = GetM_CostElement_ID();
            if (M_CostElement_ID == 0)
                return null;
            return VAdvantage.Model.MCostElement.Get(GetCtx(), M_CostElement_ID);
        }

        /**
         * 	Before Save
         *	@param newRecord new
         *	@return true if can be saved
         */
        protected override bool BeforeSave(bool newRecord)
        {
            VAdvantage.Model.MCostElement ce = GetCostElement();
            //	Check if data entry makes sense
            if (_manual)
            {
                VAdvantage.Model.MAcctSchema as1 = new VAdvantage.Model.MAcctSchema(GetCtx(), GetC_AcctSchema_ID(), null);
                String CostingLevel = as1.GetCostingLevel();
                MProduct product = MProduct.Get(GetCtx(), GetM_Product_ID());
                MProductCategoryAcct pca = MProductCategoryAcct.Get(GetCtx(),
                    product.GetM_Product_Category_ID(), as1.GetC_AcctSchema_ID(), null);
                if (pca.GetCostingLevel() != null)
                    CostingLevel = pca.GetCostingLevel();
                if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_Client.Equals(CostingLevel))
                {
                    if (GetAD_Org_ID() != 0 || GetM_AttributeSetInstance_ID() != 0)
                    {
                        log.SaveError("CostingLevelClient", "");
                        return false;
                    }
                }
                else if (VAdvantage.Model.MAcctSchema.COSTINGLEVEL_BatchLot.Equals(CostingLevel))
                {
                    if (GetM_AttributeSetInstance_ID() == 0
                        && ce.IsCostingMethod())
                    {
                        log.SaveError("FillMandatory", Msg.GetElement(GetCtx(), "M_AttributeSetInstance_ID"));
                        return false;
                    }
                    if (GetAD_Org_ID() != 0)
                        SetAD_Org_ID(0);
                }
            }

            //	Cannot enter calculated
            if (_manual && ce != null && ce.IsCalculated())
            {
                log.SaveError("Error", Msg.GetElement(GetCtx(), "IsCalculated"));
                return false;
            }
            //	Percentage
            if (ce != null)
            {
                if (ce.IsCalculated()
                    || VAdvantage.Model.MCostElement.COSTELEMENTTYPE_Material.Equals(ce.GetCostElementType())
                    && Env.Signum(GetPercentCost()) != 0)
                    SetPercentCost(Env.ZERO);
            }
            if (Env.Signum(GetPercentCost()) != 0)
            {
                if (Env.Signum(GetCurrentCostPrice()) != 0)
                    SetCurrentCostPrice(Env.ZERO);
                if (Env.Signum(GetFutureCostPrice()) != 0)
                    SetFutureCostPrice(Env.ZERO);
                if (Env.Signum(GetCumulatedAmt()) != 0)
                    SetCumulatedAmt(Env.ZERO);
                if (Env.Signum(GetCumulatedQty()) != 0)
                    SetCumulatedQty(Env.ZERO);
            }
            return true;
        }


        /**
         * 	Before Delete
         *	@return true
         */
        protected override bool BeforeDelete()
        {
            return true;
        }

        /**
         * 	Test
         *	@param args ignored
         */
        //public static void main (String[] args)
        //{
        //    /**
        //    DELETE M_Cost c
        //    WHERE EXISTS (SELECT * FROM M_CostElement ce 
        //        WHERE c.M_CostElement_ID=ce.M_CostElement_ID AND ce.IsCalculated='Y')
        //    /
        //    UPDATE M_Cost
        //      SET CumulatedAmt=0, CumulatedQty=0
        //    /  
        //    UPDATE M_CostDetail
        //      SET Processed='N'
        //    WHERE Processed='Y'
        //    /
        //    COMMIT
        //    /
        //    **/

        //    Vienna.startup(true);
        //    VAdvantage.Model.MClient client = VAdvantage.Model.MClient.Get(Env.GetCtx(), 11);	//	GardenWorld
        //    create(client);

        //}	//	main


    }
}
