import octoprint.plugin
import flask
import octoprint.filemanager.util


class TiProjektPlugin(octoprint.plugin.SimpleApiPlugin,
                       octoprint.plugin.SettingsPlugin):

    def __init__(self):
        super().__init__()

    def on_api_get(self, request):

        if not self._printer.get_state_id() == "PRINTING":
            return flask.jsonify(Error="Not printing")

        job_data = self._printer.get_current_job()

        file_data = job_data["file"]
        file_path = file_data["path"]

        file_path = self._settings.getBaseFolder("base") + "/uploads/" + file_path


        info = False
        text = ""

        with open(file_path, "r") as input_file:
            for line in input_file:
                if line.startswith(";obj"):
                    line = line.replace(";obj", "")
                    text = text + line
                    info = True

        if info:
            return flask.Response(text, mimetype="text/plain")
        else:
            return flask.jsonify(Error="No obj available")


__plugin_name__ = "ti_projekt"
__plugin_version__ = "1.0.0"
__plugin_description__ = "Extracts obj from modified gcode via http get"
__plugin_pythoncompat__ = ">=3.4,<4"
__plugin_implementation__ = TiProjektPlugin()

