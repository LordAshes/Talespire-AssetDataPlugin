# Asset Data Plugin

This unofficial TaleSpire dependency plugin for storing data regarding sources like minis, boards, campaigns, groups, and
anything else that has a unique id associated with it. Provides subscription functionality for notifications when data has
changed which supports both specific keys and key patterns allowing for a range of subscriptions ranging from very specific
to very broad. Supports both persistent and non-persistent data. Persistent data will stay associated with the source until
removed and thus can be reloaded on board load or when a client connects. Non-persistent data allows the exchange of messages
which should not be store. Such messages can be processed by anyone subscribing to them but are not stored so late connecting
client or board load will not re-trigger them.

Automatically implements package sending so that long messages are automatically broken up into smaller messages, sent and
re-asselmbed by the recipient. This means there isn't any real limit on the size of the content that can be sent. 

This plugin combines the functionality of Stat Messaging, Board Persistent and a small part of Chat Service into a single
dependency plugin.  

This plugin, like all others, is free but if you want to donate, use: http://LordAshes.ca/TalespireDonate/Donate.php

## Change Log

```
1.2.4: Minor bug fixes
1.2.3: Improved debug messages to better track flow
1.2.2: Added visual message if no message distribution plugins are found
1.2.2: Bug fix for CleaInfo not clearing persistence file 
1.2.1: Minor parameter type bug fix
1.2.0: Added Legacy Stat Messaging support if Stat Messaging is present. This allows Asset Data Plugin to get Stat Messaging
       messages and provide them to client via the Asset Data Plugin interface. Legacy write is also possible.
1.1.0: Added support for other message distribution systems besides ChatService such as RPC.
1.1.0: Migrated to Campaign files for storage.
1.0.0: Initial release
```

## Install

Use R2ModMan or similar installer to install this plugin.

This is a dependency plugin and thus is not used directly by the user. It is used by other plugins to implement various
communication and data storage features.

Note: You need at least one message distribution plugin (e.g. ChatService plugin or RPC plugin) to make this plugin work.
Currently the plugin will use the first plugin in the configured list that exists on the system. If you have multiple
message distribution plugins, you can select the preferred plugin by re-ordering the list.

## Usage

This plugin stores information about a source using keys. A key is just a name for a piece of information and is used
to identify that piece of information in read requests, write requests and notifications. For example, consider a green
ball object. The source or asset would the ball which may have multiple keys of information such as color, size, shape
and so on. If we want to reference the color information, for example, we would need to provide the read request with
both the source (asset), in this case the ball, and the piece of information (key), in this case color. 

```
Subscribe(*key*, *callback*);
```

This is a static method so you don't need to initialize any class to do it. This method triggers the specified callback
when the specified key changes on any asset. The key can be a specific key or a key pattern that uses wild cards. The
most generic pattern would be * which would subscribe to all messages being set. While each plugin can subscribe to all
messages and then process the ones it is interested in, this is not efficient. So instead plugins will typically use more
specific keys to narrow down the messgages. For example, if a plugin is only interested in its own messages then it
typically uses the plugin GUI as the key. Since a plugin's guid is unique, it means it will only get its own messages.
Similarly, if a plugin wants to read messages from a different plugin, it can narrow down the messages by subscribing
to the key that matches the other plugin's Guid. This is the recommended convention but not a forced convention. If a
plugin requires multple keys, typically the plugin Guid is followed by a dot and a further divison of the key. This
allows easy subscription to all the plugin's messages by subscribing to the plugin Guid followed by a *.

Wildcards:

``*`` = Any number of characters including none.
``?`` = Any one character


```
SetInfo(*asset*, *key*, *content*);
```

Sets the value of the specified key for the specified asset to specified content.
Unlike subscriptions, when setting values, the key must be specific key and not a key pattern.
This method causes notifications to be sent out to any clients that have subscribed to this key or whose key pattern
matches this key. This method stores the value under the give key for the given asset for future use and thus the value
will be available to new subscribers and or on board load.  

To clear a key that is no loner needed, use the following code:

```
ClearInfo(*asset*, *keyName*);
```

It should be noted that writing empty content and clearing the content are not the same. Writing an empty value is
treated the same as writing a non-empty value, the asset key will still exist in memory. Clearing the key actually
removes the key from the initernal memory and from the persistent storage.


```
SendInfo(*key*, *content*);
```

This method is similar to SendInfo except that the message is not releated to a specific asset and the message is not
stored. Any subscribers that subscribed to the given key or whose ket patterns matches the given key will be notified
with the contents but the contents will not be stored. This method is used for real-time messaging which is not to be
retained.

```
Reset(*subscriptionId*);
```
```
Reset(*pattern*);
```

These methods perform a reset associated with the given subscription id (preferred method) or the key (or key pattern).
A reset will cause all of the stored data to be re-evaluated and notifications re-sent for any data that matches the
subscriptions. A subscription based reset will re-notify only notifications associated with the reset subscription.
A patten based reset will re-notify about any data that matches the reset pattern. There is also a general reset which
resets everything but this method should not be used and has been marked as obsolete because resetting all notifications
can have adverse affects on other plugins who are not expected a notification update. For example, a plugin using a
specific notification as a counter would advance its counter if another notification did a mass reset.


## Callback Signature

``private void Callback(AssetDataPlugin.DatumChange change)``

Where DatumChange has the following properties:

*action* indicates if the change is initial info, new info, modified info or removed info.

*source* indicates the unique identification of the asset with which the information is associated.

*key* indicates the unique identification of the information that the change involves.

*previous* indicates the value prior to the change

*value* indicates the value after the change


## Examples

Subscribe(MyCustomPlugin.Guid, Callback);

Subscribe(MyCustomPlugin.Guid+".MyKey1", Callback);
Subscribe(MyCustomPlugin.Guid+".MyKey2", Callback);

Subscribe(MyCustomPlugin.Guid+".*", Callback);

SetInfo("1234-56789-9001", MyCustomPlugin.Guid+".MyKey1", "Hello!");
SetInfo("1234-56789-9001", MyCustomPlugin.Guid+".MyKey2", new List(){"A","B","C"} );

SendInfo(MyCustomPlugin.Guid+".MyKey2", "Good bye!")

ClearInfo("1234-56789-9001", MyCustomPlugin.Guid+".MyKey2");

## Configuring Message Distribution Plugins

The R2ModMan configruation for this plugin has a configuration which holds a list (one or more) of message distribution
plugins. The plugins are named using the full qualified name (without the version and culture) and entries are separated
by pipe characters (since commas are use in the qualified name). When this plugins starts up it will read through the
list, in order, and check if the corresponding plugin is installed. If so, it will do a basic check to see that the plugin 
had the correct methods and, if the test is passed, it will use that plugin for distributing content to other clients.

This leads to two important conclusions:

1. Additional message distribution plugins can easily be added by adding their fully qualified names into the list.
2. The preference of which plugin to use (if multiple are installed) can be changed by re-ordering the list order.

## Legacy Support

When StatMessaging is present, Asset Data will make a single subscription to Stat Messaging for all messages and then
treat any noitifications as if they were Asset Data notifications. This allows Asset Data to work with plugins that use
Stat Messaging.

For writing Legacy Stat Messaging messages, the SetInfo and ClearInfo methods have an optional Legacy boolean parameter
which defaults to false. When set true, Asset Data will write the information out to Stat Messaging thus allowing Asset
Data plugin to trigger plugins which use Stat Messaging.

## Reflection Subscription

An alternative subscription method was added providing a relfection friendly subscription method. This method does the
same a the regular subscription method except the instead of passing in the callback function itself, the method takes
the name of the callback (static) type and the name of the callback (static) method. This allows this method to be used
with reflection when trying to implement soft dependency on Asset Data plugin.  

