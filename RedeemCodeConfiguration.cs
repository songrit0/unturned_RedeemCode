using System.Xml.Serialization;
using Rocket.API;

namespace RedeemCode
{
    /// <summary>A chat message with a colour. Placeholders: {code}, {items}.</summary>
    public sealed class Message
    {
        [XmlAttribute] public string Text;
        [XmlAttribute] public string Color;
        public Message() { }
        public Message(string text, string color) { Text = text; Color = color; }
    }

    public sealed class DatabaseSection
    {
        public string ConnectionString;
        /// <summary>Prefix for the three tables: &lt;prefix&gt;codes, &lt;prefix&gt;code_items, &lt;prefix&gt;redemptions.</summary>
        public string TablePrefix;
    }

    /// <summary>
    /// RedeemCode config. RocketMod serializes these public fields to/from
    /// <c>Plugins/RedeemCode/RedeemCode.configuration.xml</c>.
    /// </summary>
    public sealed class RedeemCodeConfiguration : IRocketPluginConfiguration
    {
        public DatabaseSection Database;

        /// <summary>Safety cap on how many of a single item one code entry may grant.</summary>
        public int MaxAmountPerItem;

        public Message MsgUsage;
        public Message MsgNotFound;
        public Message MsgDisabled;
        public Message MsgExpired;
        public Message MsgAlreadyUsed;
        public Message MsgCapReached;
        public Message MsgSuccess;
        public Message MsgItemDropped;
        public Message MsgError;

        public void LoadDefaults()
        {
            Database = new DatabaseSection
            {
                ConnectionString = "SERVER=localhost;DATABASE=unturned;UID=root;PASSWORD=123456",
                TablePrefix = "rc_"
            };
            MaxAmountPerItem = 100;

            MsgUsage = new Message(
                "Usage: /code <code> | วิธีใช้: /code <โค้ด>", "yellow");
            MsgNotFound = new Message(
                "Invalid code | โค้ดไม่ถูกต้อง", "red");
            MsgDisabled = new Message(
                "This code is disabled | โค้ดนี้ถูกปิดใช้งาน", "red");
            MsgExpired = new Message(
                "This code has expired | โค้ดนี้หมดอายุแล้ว", "red");
            MsgAlreadyUsed = new Message(
                "You already used this code | คุณใช้โค้ดนี้ไปแล้ว", "red");
            MsgCapReached = new Message(
                "This code is fully claimed | โค้ดนี้ถูกใช้ครบจำนวนแล้ว", "red");
            MsgSuccess = new Message(
                "✅ Redeemed code {code}! Items added | รับของจากโค้ด {code} แล้ว", "green");
            MsgItemDropped = new Message(
                "Inventory full - some items dropped at your feet | กระเป๋าเต็ม บางชิ้นตกที่พื้น", "yellow");
            MsgError = new Message(
                "Something went wrong, try again later | เกิดข้อผิดพลาด ลองใหม่ภายหลัง", "red");
        }
    }
}
