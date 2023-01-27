
    var connection = new signalR.HubConnectionBuilder().withUrl("/adminHub").build();

    $(function ()
    {
        connection.start().then(function ()
        {
            console.log("Connecting to adminHub");
        }).catch(function (err)
        {
            return console.error(err.toString());
        });
    });

    connection.on("ReceivedMessage", function (type, message, status)
    {
        var icon = GetStatusContent(status);
        if (type == 'date')
        {
            $("#loadMessageDate").html(message);
        }
        else if (type == 'download')
        {
            $("#loadMessageDownload").html(message);
            $("#downloadStatus").show();
            $("#downloadStatus").attr("src", icon);

        }
        else if (type == 'import')
        {
            $("#loadMessageImport").html(message);
            $("#importStatus").show();
            $("#importStatus").attr("src", icon);
        }
        else if (type == 'downloadsCount')
        {
            console.log("downloads Count = " + message);
            $("#DownloadsCount").show();
            $("#filesDownloaded").text(message);
        }
        else if (type == 'importsCount')
        {
            $("#ImportsCount").show();
            $("#filesImported").text(message);
        }
    });

    function GetStatusContent(status)
    {
        var content = '';
        if (status == 'started')
        {
            content = "/img/processing.gif";
        }
        else if (status == 'completed')
        {
            content = "/img/completed.png";
        }
        else if (status == 'error')
        {
            content = "/img/error.png";
        }
        return content;
    }
