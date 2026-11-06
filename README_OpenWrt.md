# Install Lighttpd, Nginx or Caddy on OpenWrt and use URL rewriting to redirect to the China CDN

For the principle behind the speed-up see the PC app [XboxDownload](https://github.com/skydevil88/XboxDownload "XboxDownload"); it also accelerates PC Microsoft Store downloads.
Routers that support Docker (some Xiaomi, TP-LINK models, etc.) and NAS devices can also run Nginx in Docker with a macvlan network (own IP address) to redirect to the China download servers.

To use this method the router's IPv6 must be turned off for the Xbox. If other devices need IPv6, add the following rules under *Network → Firewall → Custom Rules* to block IPv6 for the Xbox only (replace `AA:BB:CC:DD:EE:FF` with the Xbox's MAC address):

```bash
ip6tables -I INPUT -p udp -m mac --mac-source AA:BB:CC:DD:EE:FF --dport 547 -j DROP
ip6tables -I INPUT -p icmpv6 -m mac --mac-source AA:BB:CC:DD:EE:FF -j DROP
```

In the tutorials below replace `192.168.1.1` with the IP address of your OpenWrt router.

## Method 1: Lighttpd
Video tutorial (Bilibili): https://www.bilibili.com/video/BV1wP4y1G7zf

1. Change the uhttpd port (afterwards the OpenWrt admin page is at `192.168.1.1:8080`; this frees port 80 for Lighttpd/Nginx)
```bash
vi /etc/config/uhttpd
# Press i to enter insert mode,
list listen_http '0.0.0.0:80' --> list listen_http '0.0.0.0:8080'
list listen_http '[::]:80' --> list listen_http '[::]:8080'
# Press ESC, type :wq to save and quit vi, then restart uhttpd
/etc/init.d/uhttpd restart
```
![Fig 1](images/OpenWrp/Op1.png)

2. Install lighttpd
```bash
opkg update
opkg install lighttpd lighttpd-mod-redirect lighttpd-mod-proxy
```

3. Configure lighttpd
```bash
vi /etc/lighttpd/lighttpd.conf
# Press i for insert mode, paste the code below (Shift+Insert), press ESC, type :wq to save and quit vi

server.modules = ( "mod_redirect" )
$HTTP["host"] =~ "^(assets1|assets2|d1|d2|xvcf1|xvcf2)\.xboxlive\.com$" {
	url.redirect = ( "(.*)" => "http://assets1.xboxlive.cn$1" )
}
$HTTP["host"] =~ "^(dlassets|dlassets2)\.xboxlive\.com$" {
	url.redirect = ( "(.*)" => "http://dlassets.xboxlive.cn$1" )
}
# (Optional) forward all other requests to port 8080 so the admin page works without a port number
server.modules +=( "mod_proxy")
proxy.server = ( "" => (( "host" => "127.0.0.1", "port" => 8080	)))

# Start lighttpd and enable it at boot
/etc/init.d/lighttpd start
/etc/init.d/lighttpd enable
# Restart lighttpd
/etc/init.d/lighttpd restart
```
![Fig 2](images/OpenWrp/Op2.png)

4. In the OpenWrt admin page go to *Network → Hostnames* and add these hostnames:
```bash
xvcf1.xboxlive.com			192.168.1.1
xvcf2.xboxlive.com			192.168.1.1
assets1.xboxlive.com			192.168.1.1
assets2.xboxlive.com			192.168.1.1
d1.xboxlive.com				192.168.1.1
d2.xboxlive.com				192.168.1.1
dlassets.xboxlive.com			192.168.1.1
dlassets2.xboxlive.com			192.168.1.1

# Use the XboxDownload speed test to find the fastest IP for xxx.xxx.xxx.xxx. There are three groups of IPs; most China IPs
# are not interchangeable between groups, so test each one (Akamai IPs work for all groups).
# Group 1: main download domains (shared by PC and consoles)
assets1.xboxlive.cn			xxx.xxx.xxx.xxx
assets2.xboxlive.cn			xxx.xxx.xxx.xxx
d1.xboxlive.cn				xxx.xxx.xxx.xxx
d2.xboxlive.cn				xxx.xxx.xxx.xxx

# Group 2: download domains for some older Xbox One games (no longer used by new PC/console games)
dlassets.xboxlive.cn			xxx.xxx.xxx.xxx
dlassets2.xboxlive.cn			xxx.xxx.xxx.xxx

# Group 3: apps and some PC games use these domains
dl.delivery.mp.microsoft.com		xxx.xxx.xxx.xxx
tlu.dl.delivery.mp.microsoft.com	xxx.xxx.xxx.xxx
```

![Fig 3](images/OpenWrp/Op3.png)
![Fig 4](images/SpeedTest.png)


Finally, in a browser (turn IPv6 off on the PC, and preferably use a private/InPrivate window to avoid caching) open
http://xvcf1.xboxlive.com/Z/routing/extraextralarge.txt . If the address changes to http://assets1.xboxlive.cn/Z/routing/extraextralarge.txt the redirect works; now start your Xbox and enjoy fast downloads.





## Method 2: Nginx
Note: firmware that already uses Nginx instead of uhttpd can skip steps 1 and 2.
1. Change the uhttpd port. This time use the admin page (*System → Web Admin*), set the port to 8080, save and reboot.
If there is no such page, change the uhttpd port as described in the Lighttpd method.
![Fig 2.1](images/OpenWrp/Op2.1.png)

2. After the reboot go to *System → Software*, refresh the package list, type `nginx` in the filter and install nginx.
![Fig 2.2](images/OpenWrp/Op2.2.png)
![Fig 2.3](images/OpenWrp/Op2.3.png)

3. Connect to the router with SSH
```bash
vi /etc/nginx/conf.d/xbox.conf
# Press i for insert mode, paste the code below (Shift+Insert), press ESC, type :wq to save and quit vi
# If nginx fails to start, add: server_names_hash_bucket_size 64; and if that is not enough raise it in multiples of 32 (128, 256)

server_names_hash_bucket_size 64;
server {
        listen 80;
        server_name assets1.xboxlive.com assets2.xboxlive.com d1.xboxlive.com d2.xboxlive.com xvcf1.xboxlive.com xvcf2.xboxlive.com dlassets.xboxlive.com dlassets2.xboxlive.com;
        if ($host !~* "^dlassets2?\.xboxlive\.com$") {
                return 302 http://assets1.xboxlive.cn$request_uri;
        }
        if ($host ~* "^dlassets2?\.xboxlive\.com$") {
                return 302 http://dlassets.xboxlive.cn$request_uri;
        }
}

# Start nginx and enable it at boot
/etc/init.d/nginx start
/etc/init.d/nginx enable
# Restart nginx
/etc/init.d/nginx restart
```
![Fig 2.4](images/OpenWrp/Op2.4.png)
![Fig 2.5](images/OpenWrp/Op2.5.png)

4. Open the OpenWrt admin page (http://192.168.1.1:8080), go to *Network → Hostnames* and add these hostnames:
```bash
xvcf1.xboxlive.com			192.168.1.1
xvcf2.xboxlive.com			192.168.1.1
assets1.xboxlive.com			192.168.1.1
assets2.xboxlive.com			192.168.1.1
d1.xboxlive.com				192.168.1.1
d2.xboxlive.com				192.168.1.1
dlassets.xboxlive.com			192.168.1.1
dlassets2.xboxlive.com			192.168.1.1

# Use the XboxDownload speed test to find the fastest IP for xxx.xxx.xxx.xxx. There are three groups of IPs; most China IPs
# are not interchangeable between groups, so test each one (Akamai IPs work for all groups).
# Group 1: main download domains (shared by PC and consoles)
assets1.xboxlive.cn			xxx.xxx.xxx.xxx
assets2.xboxlive.cn			xxx.xxx.xxx.xxx
d1.xboxlive.cn				xxx.xxx.xxx.xxx
d2.xboxlive.cn				xxx.xxx.xxx.xxx

# Group 2: download domains for some older Xbox One games (no longer used by new PC/console games)
dlassets.xboxlive.cn			xxx.xxx.xxx.xxx
dlassets2.xboxlive.cn			xxx.xxx.xxx.xxx

# Group 3: apps and some PC games use these domains
dl.delivery.mp.microsoft.com		xxx.xxx.xxx.xxx
tlu.dl.delivery.mp.microsoft.com	xxx.xxx.xxx.xxx
```

![Fig 3](images/OpenWrp/Op3.png)
![Fig 4](images/SpeedTest.png)


Finally, in a browser (turn IPv6 off on the PC, and preferably use a private/InPrivate window to avoid caching) open
http://xvcf1.xboxlive.com/Z/routing/extraextralarge.txt . If the address changes to http://assets1.xboxlive.cn/Z/routing/extraextralarge.txt the redirect works; now start your Xbox and enjoy fast downloads.

## Method 3: Caddy

Caddyfile:

```
http://assets1.xboxlive.com, http://assets2.xboxlive.com, http://d1.xboxlive.com, http://d2.xboxlive.com, http://xvcf1.xboxlive.com, http://xvcf2.xboxlive.com {
        redir http://assets1.xboxlive.cn{uri}
}
http://dlassets.xboxlive.com, http://dlassets2.xboxlive.com {
        redir http://dlassets.xboxlive.cn{uri}
}
```

As with methods 1 and 2, hijack the `*.xboxlive.com` domains in the router so they resolve to the Caddy server's IP.

## Other: IIS
https://tieba.baidu.com/p/7151558145
