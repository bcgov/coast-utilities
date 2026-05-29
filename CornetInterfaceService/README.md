# CornetInterfaceService

This is an interface between Cornet and Dynamics. It's a clone of the CASInterfaceService where the prototype was developed, with the CAS interface specific code removed so that the only function remaining is Cornet related.

## Reverse Proxy Configuration

_See https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files._

Proxies all requests to `<host>/CorVSUDynAPI/*` to `<new_host>/CorVSUDynAPI/*`.

```
{
  "ReverseProxy": {
    "Routes": {
      "CorVSUDynAPI": {
        "ClusterId": "CorVSUDynAPI",
        "AuthorizationPolicy": "default",
        "Match": {
          "Path": "/CorVSUDynAPI/{**remainder}"
        }
      }
    },
    "Clusters": {
      "CorVSUDynAPI": {
        "Destinations": {
          "CorVSUDynAPI": {
            "Address": "https://wsgw.dev.jag.gov.bc.ca"
          }
        }
      }
    }
  }
}
```
