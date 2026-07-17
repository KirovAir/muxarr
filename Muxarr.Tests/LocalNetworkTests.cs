using System.Net;
using Muxarr.Web.Authentication;

namespace Muxarr.Tests;

[TestClass]
public class LocalNetworkTests
{
    [TestMethod]
    [DataRow("127.0.0.1", true)]
    [DataRow("127.5.5.5", true)]
    [DataRow("10.0.0.1", true)]
    [DataRow("10.255.255.255", true)]
    [DataRow("172.16.0.1", true)]
    [DataRow("172.31.255.255", true)]
    [DataRow("172.15.255.255", false)]
    [DataRow("172.32.0.1", false)]
    [DataRow("192.168.1.1", true)]
    [DataRow("192.169.0.1", false)]
    [DataRow("169.254.1.1", true)]
    [DataRow("169.253.0.1", false)]
    [DataRow("8.8.8.8", false)]
    [DataRow("::1", true)]
    [DataRow("fe80::1", true)]
    [DataRow("fc00::1", true)]
    [DataRow("fd12:3456:789a::1", true)]
    [DataRow("2001:4860:4860::8888", false)]
    [DataRow("::ffff:192.168.1.10", true)]
    [DataRow("::ffff:8.8.8.8", false)]
    public void ClassifiesAddress(string ip, bool expected)
    {
        Assert.AreEqual(expected, LocalNetwork.IsLocalAddress(IPAddress.Parse(ip)));
    }

    [TestMethod]
    public void NullAddress_IsNotLocal()
    {
        Assert.IsFalse(LocalNetwork.IsLocalAddress(null));
    }
}
