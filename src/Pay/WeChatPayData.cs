﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Xml;
using Newtonsoft.Json;

namespace Sugar.WeChat
{
    /// <summary>
    /// 微信支付协议接口数据类，所有的API接口通信都依赖这个数据结构，
    /// 在调用接口之前先填充各个字段的值，然后进行接口通信，
    /// 这样设计的好处是可扩展性强，用户可随意对协议进行更改而不用重新设计数据结构，
    /// 还可以随意组合出不同的协议数据包，不用为每个协议设计一个数据包结构
    /// </summary>
    public class WeChatPayData
    {
        public const string SIGN_TYPE_MD5 = "MD5";
        public const string SIGN_TYPE_HMAC_SHA256 = "HMAC-SHA256";

        //采用排序的Dictionary的好处是方便对数据包进行签名，不用再签名之前再做一次排序
        private SortedDictionary<string, object> m_values = new SortedDictionary<string, object>();

        /**
        * 设置某个字段的值
        * @param key 字段名
         * @param value 字段值
        */
        public void SetValue(string key, object value)
        {
            m_values[key] = value;
        }

        /**
        * 根据字段名获取某个字段的值
        * @param key 字段名
         * @return key对应的字段值
        */
        public object GetValue(string key)
        {
            object o = null;
            m_values.TryGetValue(key, out o);
            return o;
        }

        /**
         * 判断某个字段是否已设置
         * @param key 字段名
         * @return 若字段key已被设置，则返回true，否则返回false
         */
        public bool IsSet(string key)
        {
            object o = null;
            m_values.TryGetValue(key, out o);
            if (null != o)
                return true;
            else
                return false;
        }

        /**
        * @将Dictionary转成xml
        * @return 经转换得到的xml串
        * @throws WxPayException
        **/
        public string ToXml()
        {
            //数据为空时不能转化为xml格式
            if (0 == m_values.Count)
            {
                throw new WeChatPayException("WeChatPayData数据为空!");
            }

            string xml = "<xml>";
            foreach (KeyValuePair<string, object> pair in m_values)
            {
                //字段值不能为null，会影响后续流程
                if (pair.Value == null)
                {
                    throw new WeChatPayException("WeChatPayData内部含有值为null的字段!");
                }

                if (pair.Value.GetType() == typeof(int))
                {
                    xml += "<" + pair.Key + ">" + pair.Value + "</" + pair.Key + ">";
                }
                else if (pair.Value.GetType() == typeof(string))
                {
                    xml += "<" + pair.Key + ">" + "<![CDATA[" + pair.Value + "]]></" + pair.Key + ">";
                }
                else//除了string和int类型不能含有其他数据类型
                {
                    throw new WeChatPayException($"WeChatPayData字段数据类型错误!{pair.Key}:{pair.Value}");
                }
            }
            xml += "</xml>";
            return xml;
        }

        /**
        * @将xml转为WxPayData对象并返回对象内部的数据
        * @param string 待转换的xml串
        * @return 经转换得到的Dictionary
        * @throws WxPayException
        */
        public SortedDictionary<string, object> FromXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                throw new WeChatPayException("将空的xml串转换为WxPayData不合法!");
            }


            XmlDocument xmlDoc = new XmlDocument() { XmlResolver = null };
            xmlDoc.LoadXml(xml);
            XmlNode xmlNode = xmlDoc.FirstChild;//获取到根节点<xml>
            XmlNodeList nodes = xmlNode.ChildNodes;
            foreach (XmlNode xn in nodes)
            {
                XmlElement xe = (XmlElement)xn;
                m_values[xe.Name] = xe.InnerText;//获取xml的键值对到WxPayData内部的数据中
            }
            //2015-06-29 错误是没有签名
            if (m_values["return_code"] != "SUCCESS")
            {
                return m_values;
            }
            CheckSign();//验证签名,不通过会抛异常 
            return m_values;
        }

        /**
        * @Dictionary格式转化成url参数格式
        * @ return url格式串, 该串不包含sign字段值
        */
        public string ToUrl()
        {
            string buff = "";
            foreach (KeyValuePair<string, object> pair in m_values)
            {
                if (pair.Value == null)
                {
                    throw new WeChatPayException("WeChatPayData内部含有值为null的字段!");
                }

                if (pair.Key != "sign" && pair.Value.ToString() != "")
                {
                    buff += pair.Key + "=" + pair.Value + "&";
                }
            }
            buff = buff.Trim('&');
            return buff;
        }


        /**
        * @Dictionary格式化成Json
         * @return json串数据
        */
        public string ToJson()
        {
            string jsonStr = JsonConvert.SerializeObject(m_values);
            return jsonStr;

        }

        /**
        * @values格式化成能在Web页面上显示的结果（因为web页面上不能直接输出xml格式的字符串）
        */
        public string ToPrintStr()
        {
            string str = "";
            foreach (KeyValuePair<string, object> pair in m_values)
            {
                if (pair.Value == null)
                {
                    throw new WeChatPayException("WeChatPayData内部含有值为null的字段!");
                }


                str += string.Format("{0}={1}\n", pair.Key, pair.Value.ToString());
            }
            str = HttpUtility.HtmlEncode(str);
            return str;
        }


        /**
        * @生成签名，详见签名生成算法
        * @return 签名, sign字段不参加签名
        */
        private string MakeSign(string signType, string key)
        {
            //转url格式
            string str = ToUrl();
            //在string后加入API KEY
            str += "&key=" + key;
            if (signType == SIGN_TYPE_MD5)
            {
                var md5 = MD5.Create();
                var bs = md5.ComputeHash(Encoding.UTF8.GetBytes(str));
                var sb = new StringBuilder();
                foreach (byte b in bs)
                {
                    sb.Append(b.ToString("x2"));
                }
                //所有字符转为大写
                return sb.ToString().ToUpper();
            }
            else if (signType == SIGN_TYPE_HMAC_SHA256)
            {
                return CalcHMACSHA256Hash(str, key);
            }
            else
            {
                throw new WeChatPayException("sign_type 不合法");
            }
        }

        /**
        * @生成签名，详见签名生成算法
        * @return 签名, sign字段不参加签名 SHA256
        */
        private string MakeSign(string key)
        {
            return MakeSign(SIGN_TYPE_MD5, key);
        }

        public string MakeSign(WeChatPayOptions option)
        {
            if (option.IsDebug)
            {
                string url = "https://api.mch.weixin.qq.com/sandboxnew/pay/getsignkey";
                WeChatPayData signkeyPayData = new WeChatPayData();
                signkeyPayData.SetValue("mch_id", option.MchID);//商户号
                signkeyPayData.SetValue("nonce_str", Guid.NewGuid().ToString("N").ToLower());//随机字符串
                signkeyPayData.SetValue("sign", signkeyPayData.MakeSign("MD5", option.Key));
                string response = (new HttpService(option)).Post(signkeyPayData.ToXml(), url, false, 6);//调用HTTP通信接口提交数据
                WeChatPayData result = new WeChatPayData();
                result.FromXml(response);
                if (result.GetValue("return_code")?.ToString() == "SUCCESS")
                {
                    var key = result.GetValue("sandbox_signkey")?.ToString();
                    return MakeSign(key);
                }
                throw new WeChatPayException("获取沙盒签名失败" + result.ToJson());
            }
            if ((this.GetValue("signType") ?? this.GetValue("sign_type"))?.ToString() != "MD5")
                return MakeSign(SIGN_TYPE_HMAC_SHA256, option.Key);
            return MakeSign(option.Key);
        }

        /**
        * 
        * 检测签名是否正确
        * 正确返回true，错误抛异常
        */
        public bool CheckSign(string signType)
        {
            //如果没有设置签名，则跳过检测
            if (!IsSet("sign"))
            {
                throw new WeChatPayException("WeChatPayData签名存在但不合法!");
            }
            //如果设置了签名但是签名为空，则抛异常
            else if (GetValue("sign") == null || GetValue("sign").ToString() == "")
            {
                throw new WeChatPayException("WeChatPayData签名存在但不合法!");
            }

            //获取接收到的签名
            string return_sign = GetValue("sign").ToString();

            //在本地计算新的签名
            string cal_sign = MakeSign(signType);

            if (cal_sign == return_sign)
            {
                return true;
            }

            throw new WeChatPayException("WeChatPayData签名验证错误!");
        }



        /**
        * 
        * 检测签名是否正确
        * 正确返回true，错误抛异常
        */
        public bool CheckSign()
        {
            return CheckSign(SIGN_TYPE_MD5);
        }

        /**
        * @获取Dictionary
        */
        public SortedDictionary<string, object> GetValues()
        {
            return m_values;
        }


        private string CalcHMACSHA256Hash(string plaintext, string salt)
        {
            string result = "";
            var enc = Encoding.Default;
            byte[]
            baText2BeHashed = enc.GetBytes(plaintext),
            baSalt = enc.GetBytes(salt);
            System.Security.Cryptography.HMACSHA256 hasher = new HMACSHA256(baSalt);
            byte[] baHashedText = hasher.ComputeHash(baText2BeHashed);
            result = string.Join("", baHashedText.ToList().Select(b => b.ToString("x2")).ToArray());
            return result;
        }




    }
}
