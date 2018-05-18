using Microsoft;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MixerChat : MonoBehaviour
{
    public Text text;
    // The number of lines of chat to print.
    public int linesToShow;

    private string _userName = string.Empty;
    private string _channelID = "";
    private string _baseURL = "https://mixer.com/api/v1/chats/";
    private string _channelURL = string.Empty;
    private bool _isDirty;
    private List<string> _messagesToPrint;

    private string _websocketURL;
    private string _authKey;
    private Websocket _websocket;

    private const string _WS_MESSAGE_KEY_ENDPOINTS = "endpoints";

    // Use this for initialization
    void Start()
    {
        _messagesToPrint = new List<string>();

        MixerInteractive.OnGoInteractive += OnGoInteractive;
        Application.runInBackground = true;
    }

    private void OnGoInteractive(object sender, Microsoft.Mixer.InteractiveEventArgs e)
    {
        StartCoroutine("GetUserInfoRoutine");
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButton("Fire1"))
        {
            MixerInteractive.GoInteractive();
        }
        UpdateChatMessageDisplay();
    }

    private void UpdateChatMessageDisplay()
    {
        if (!_isDirty || _messagesToPrint.Count == 0)
        {
            return;
        }
        _isDirty = false;
        text.text = string.Empty;
        Debug.Log(_messagesToPrint.Count);
        foreach (string messages in _messagesToPrint)
        {
            text.text += messages + "\n";
        }
    }

    private IEnumerator GetUserInfoRoutine()
    {
        string getUserInfoUrl = "https://mixer.com/api/v1/users/current";
        using (UnityWebRequest request = UnityWebRequest.Get(getUserInfoUrl))
        {
            request.SetRequestHeader("Authorization", MixerInteractive.Token);
            yield return request.SendWebRequest();
            if (request.isNetworkError)
            {
                Debug.Log("Error: Could not retrieve user info. " + request.error);
            }
            else // Success
            {
                string channelIDRawJson = request.downloadHandler.text;
                ParseUserInfo(channelIDRawJson);
                StartCoroutine("InitializeCoRoutine");
            }
        }
    }

    private void ParseUserInfo(string userInfoRawJson)
    {
        using (StringReader stringReader = new StringReader(userInfoRawJson))
        using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
        {
            while (jsonReader.Read())
            {
                if (_channelID != string.Empty)
                {
                    break;
                }
                if (jsonReader.Value != null)
                {
                    if (jsonReader.Value.ToString() == "channel")
                    {
                        ParseGetUserId(jsonReader);
                    }
                }
            }
        }
    }

    private void ParseGetUserId(JsonReader jsonReader)
    {
        _channelID = string.Empty;
        while (jsonReader.Read())
        {
            if (_channelID != string.Empty)
            {
                break;
            }
            if (jsonReader.Value != null)
            {
                if (jsonReader.Value.ToString() == "id")
                {
                    jsonReader.Read();
                    _channelID = jsonReader.Value.ToString();
                }
            }
        }
    }

    private IEnumerator InitializeCoRoutine()
    {
        _channelURL = _baseURL + _channelID;
        using (UnityWebRequest request = UnityWebRequest.Get(_channelURL))
        {
            yield return request.SendWebRequest();
            if (request.isNetworkError)
            {
                Debug.Log("Error: Could not retrieve websocket URL to connect to chat. " + request.error);
            }
            else // Success
            {
                string websocketHostsJson = request.downloadHandler.text;
                ParseWebsocketConnectionInformation(websocketHostsJson);
                // Find a websocket to connect to.
                Dictionary<string, string> headers = new Dictionary<string, string>();
                _websocket = GetComponent<Websocket>();
                _websocket.OnOpen += _websocket_OnOpen;
                _websocket.OnMessage += _websocket_OnMessage;
                _websocket.OnError += _websocket_OnError;
                _websocket.OnClose += _websocket_OnClose;
                _websocket.Open(new Uri(_websocketURL));
            }
        }
    }

    private void _websocket_OnClose(object sender, CloseEventArgs e)
    {
        Debug.Log("Close: " + e.Reason);
    }

    private void _websocket_OnError(object sender, Microsoft.ErrorEventArgs e)
    {
        Debug.Log("Error: " + e.Message);
    }

    private void _websocket_OnMessage(object sender, MessageEventArgs e)
    {
        Debug.Log("Message: " + e.Message);
        ProcessWebSocketMessage(e.Message);
    }

    private void _websocket_OnOpen(object sender, EventArgs e)
    {
        Debug.Log("Chat websocket open event.");
    }

    private void ProcessWebSocketMessage(string message)
    {
        Debug.Log(message);
        using (StringReader stringReader = new StringReader(message))
        using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
        {
            while (jsonReader.Read())
            {
                // Look for data section
                if (jsonReader.Value != null &&
                    jsonReader.Value.ToString() == "event")
                {
                    jsonReader.Read();
                    string eventType = jsonReader.ReadAsString().ToString();
                    switch (eventType)
                    {
                        case "WelcomeEvent":
                            ProcessWelcomeMessage(jsonReader);
                            break;
                        case "ChatMessage":
                            ProcessChatMessage(jsonReader);
                            break;
                        default:
                            // No-op.
                            break;
                    }
                }
            }
        }
    }

    private void ProcessWelcomeMessage(JsonReader jsonReader)
    {
        SendAuthMessage();
    }

    private void ProcessChatMessage(JsonReader jsonReader)
    {
        // Parse the chat messages
        string chatMessage = string.Empty;
        string userName = string.Empty;
        List<MessageObject> messages = ProcessMessageFragments(jsonReader, out userName);

        // Handle chat messages and print them out
        foreach (MessageObject messageObject in messages)
        {
            switch (messageObject.type)
            {
                case ChatMessageTypes.Text:
                    chatMessage += PrintTextMessageFragment((TextData)messageObject.data);
                    break;
                case ChatMessageTypes.Emoticon:
                    // Not supported.
                    break;
                case ChatMessageTypes.Link:
                    chatMessage += PrintLinkMessageFragment((LinkData)messageObject.data);
                    break;
                default:
                    break;
            }
        }

        if (_messagesToPrint.Count > linesToShow)
        {
            _messagesToPrint.RemoveAt(0);
        }
        _messagesToPrint.Add(userName + ": " + chatMessage);
        _isDirty = true;
    }

    // We only support anonymous mode now.
    private void SendAuthMessage()
    {
        _websocket.Send(
                "{" +
                "\"type\": \"method\"," +
                "\"method\": \"auth\"," +
                "\"arguments\": [" +
                _channelID + "," +
                "   0," +
                "\"" + _authKey + "\"" +
                "]," +
                "\"id\": 0" +
                "}"
            );
    }

    private string PrintTextMessageFragment(TextData textData)
    {
        string messageFragment = string.Empty;
        messageFragment += textData.text;
        return messageFragment;
    }

    private string PrintLinkMessageFragment(LinkData linkData)
    {
        string messageFragment = string.Empty;
        messageFragment += linkData.text;
        return messageFragment;
    }

    private List<MessageObject> ProcessMessageFragments(JsonReader jsonReader, out string userName)
    {
        List<MessageObject> messages = new List<MessageObject>();
        userName = string.Empty;
        while (jsonReader.Read())
        {
            // Look for data section
            if (jsonReader.Value != null &&
                jsonReader.Value.ToString() == "data")
            {
                // Parse message object
                messages = ParseMessageObjects(jsonReader, out userName);
            }
        }
        return messages;
    }

    private enum ChatMessageTypes
    {
        Unknown,
        Text,
        Emoticon,
        Link
    }

    private struct MessageObject
    {
        public ChatMessageTypes type;
        public object data;
    }

    private struct TextData
    {
        public string data;
        public string text;
    }

    private struct EmoticonData
    {
        public EmoticonSource source;
        public string pack;
        public Rect coordinates;
        public string text;
    }

    private struct LinkData
    {
        public string url;
        public string text;
    }

    private enum EmoticonSource
    {
        BuiltIn,
        External
    }

    private List<MessageObject> ParseMessageObjects(JsonReader jsonReader, out string userName)
    {
        List<MessageObject> messageObjects = new List<MessageObject>();
        userName = string.Empty;
        while (jsonReader.Read())
        {
            // Get the user name
            if (jsonReader.Value != null &&
                jsonReader.Value.ToString() == "user_name")
            {
                jsonReader.Read();
                userName = jsonReader.Value.ToString();
            }

            // Look for data section
            if (jsonReader.TokenType == JsonToken.StartArray)
            {
                messageObjects.Add(ParseMessageObject(jsonReader));
            }
        }
        return messageObjects;
    }

    private MessageObject ParseMessageObject(JsonReader jsonReader)
    {
        Debug.Log("ParseMessageObject");

        MessageObject messageObject = new MessageObject();
        ChatMessageTypes type = ChatMessageTypes.Unknown;
        while (jsonReader.Read())
        {
            if (jsonReader.Value != null)
            {
                // Look for data section
                if (jsonReader.Value.ToString() == "type")
                {
                    jsonReader.Read();
                    if (jsonReader.Value != null)
                    {
                        switch (jsonReader.Value.ToString())
                        {
                            case "text":
                                type = ChatMessageTypes.Text;
                                break;
                            case "emoticon":
                                type = ChatMessageTypes.Emoticon;
                                Debug.Log("type:emoticon");
                                break;
                            case "link":
                                type = ChatMessageTypes.Link;
                                break;
                            default:
                                // No-op
                                break;
                        };
                        messageObject = ProcessMessageObject(jsonReader, type);
                    }
                }
            }
        }
        return messageObject;
    }

    private MessageObject ProcessMessageObject(JsonReader jsonReader, ChatMessageTypes type)
    {
        Debug.Log("ProcessMessageObject");

        MessageObject messageObject = new MessageObject();
        try
        {
            switch (type)
            {
                case ChatMessageTypes.Text:
                    messageObject = ProcessTextMessage(jsonReader);
                    break;
                case ChatMessageTypes.Emoticon:
                    messageObject = ProcessEmoticonMessage(jsonReader);
                    break;
                case ChatMessageTypes.Link:
                    messageObject = ProcessLinkMessage(jsonReader);
                    break;
                default:
                    // No-op
                    break;
            };
        }
        catch (Exception ex)
        {
            Debug.Log(ex.Message);
        }

        return messageObject;
    }

    private MessageObject ProcessTextMessage(JsonReader jsonReader)
    {
        Debug.Log("ProcessTextMessage");

        MessageObject messageObject = new MessageObject();
        messageObject.type = ChatMessageTypes.Text;
        TextData textData = new TextData();
        while (jsonReader.Read())
        {
            // Look for data section
            if (jsonReader.Value != null)
            {
                switch (jsonReader.Value.ToString())
                {
                    case "data":
                        jsonReader.ReadAsString();
                        textData.data = jsonReader.Value.ToString();
                        break;
                    case "text":
                        jsonReader.ReadAsString();
                        textData.text = jsonReader.Value.ToString();
                        break;
                    default:
                        // No-op
                        break;
                };
            }
        }
        messageObject.data = textData;
        return messageObject;
    }

    private MessageObject ProcessEmoticonMessage(JsonReader jsonReader)
    {
        Debug.Log("ProcessEmoticonMessage");

        MessageObject messageObject = new MessageObject();
        messageObject.type = ChatMessageTypes.Emoticon;
        EmoticonData emoticonData = new EmoticonData();
        while (jsonReader.Read())
        {
            // Look for data section
            if (jsonReader.Value != null)
            {
                switch (jsonReader.Value.ToString())
                {
                    case "source":
                        jsonReader.ReadAsString();
                        emoticonData.source = jsonReader.Value.ToString() == "builtin" ? EmoticonSource.BuiltIn : EmoticonSource.External;
                        break;
                    case "pack":
                        jsonReader.ReadAsString();
                        emoticonData.pack = jsonReader.Value.ToString();
                        break;
                    case "coords":
                        jsonReader.ReadAsString();
                        emoticonData.coordinates = ProcessCoordinates(jsonReader);
                        break;
                    default:
                        // No-op
                        break;
                };
            }
        }
        messageObject.data = emoticonData;
        return messageObject;
    }

    private Rect ProcessCoordinates(JsonReader jsonReader)
    {
        Debug.Log("ProcessCoordinates");
        Rect coordinates = new Rect();
        while (jsonReader.Read())
        {
            // Look for data section
            if (jsonReader.Value != null)
            {
                switch (jsonReader.Value.ToString())
                {
                    case "x":
                        jsonReader.ReadAsDouble();
                        coordinates.xMin = (float)jsonReader.Value;
                        break;
                    case "y":
                        jsonReader.ReadAsDouble();
                        coordinates.xMax = (float)jsonReader.Value;
                        break;
                    case "width":
                        jsonReader.ReadAsDouble();
                        coordinates.width = (float)jsonReader.Value;
                        break;
                    case "height":
                        jsonReader.ReadAsDouble();
                        coordinates.height = (float)jsonReader.Value;
                        break;
                    default:
                        // No-op
                        break;
                };
            }
        }
        return coordinates;
    }

    private MessageObject ProcessLinkMessage(JsonReader jsonReader)
    {
        Debug.Log("ProcessLinkMessage");
        MessageObject messageObject = new MessageObject();
        messageObject.type = ChatMessageTypes.Link;
        LinkData linkData = new LinkData();
        while (jsonReader.Read())
        {
            // Look for data section
            if (jsonReader.Value != null)
            {
                switch (jsonReader.Value.ToString())
                {
                    case "url":
                        jsonReader.ReadAsString();
                        linkData.url = jsonReader.Value.ToString();
                        break;
                    case "text":
                        jsonReader.ReadAsString();
                        linkData.text = jsonReader.Value.ToString();
                        break;
                    default:
                        // No-op
                        break;
                };
            }
        }
        messageObject.data = linkData;
        return messageObject;
    }

    private void ParseChannelID(string channelIDRawJson)
    {
        _websocketURL = string.Empty;
        _channelID = string.Empty;
        using (StringReader stringReader = new StringReader(channelIDRawJson))
        using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
        {
            while (jsonReader.Read())
            {
                if (_channelID != string.Empty)
                {
                    break;
                }
                if (jsonReader.Value != null)
                {
                    if (jsonReader.Value.ToString() == "id")
                    {
                        jsonReader.Read();
                        _channelID = jsonReader.Value.ToString();
                    }
                }
            }
        }
    }

    private void ParseWebsocketConnectionInformation(string potentialWebsocketUrlsJson)
    {
        _websocketURL = string.Empty;
        _authKey = string.Empty;
        using (StringReader stringReader = new StringReader(potentialWebsocketUrlsJson))
        using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
        {
            while (jsonReader.Read())
            {
                if (_websocketURL != string.Empty &&
                    _authKey != string.Empty)
                {
                    break;
                }
                if (jsonReader.Value != null)
                {
                    if (jsonReader.Value.ToString() == "authkey")
                    {
                        _authKey = jsonReader.Value.ToString();
                    }
                    else if (jsonReader.Value.ToString() == _WS_MESSAGE_KEY_ENDPOINTS)
                    {
                        while (jsonReader.Read())
                        {
                            if (jsonReader.TokenType == JsonToken.StartArray)
                            {
                                jsonReader.Read();
                                if (jsonReader.Value != null)
                                {
                                    _websocketURL = jsonReader.Value.ToString();
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
