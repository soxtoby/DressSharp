import java.nio.file.Files;
import java.nio.file.Path;
import java.util.regex.Pattern;

class FormatterWorker {
    public static void main(String[] args) throws Exception {
        if (args.length > 3) {
            Files.writeString(Path.of(args[1] + ".launcher"), Long.toString(ProcessHandle.current().pid()));
            var java = Path.of(System.getProperty("java.home"), "bin", System.getProperty("os.name").startsWith("Windows") ? "java.exe" : "java");
            var child = new ProcessBuilder(java.toString(), args[3], args[0], args[1], args[2]).inheritIO().start();
            System.exit(child.waitFor());
        }
        var pid = Long.toString(ProcessHandle.current().pid());
        var text = Pattern.compile("\"text\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")");
        var output = new java.io.PrintWriter(System.out, true, java.nio.charset.StandardCharsets.UTF_8);
        output.println("{\"protocol\":2,\"assemblyPath\":" + quote(args[0]) + "}");
        var input = new java.io.BufferedReader(new java.io.InputStreamReader(System.in, java.nio.charset.StandardCharsets.UTF_8));
        String line;
        while ((line = input.readLine()) != null) {
            var match = text.matcher(line);
            var payload = match.find() ? match.group(1) : quote(line);
            Files.writeString(Path.of(args[1]), pid);
            if (payload.contains("slow")) {
                while (!Files.exists(Path.of(args[2]))) Thread.sleep(5);
            }
            output.println("{\"exitCode\":0,\"stdout\":" + quote(pid) + ",\"stderr\":" + payload + "}");
        }
    }

    static String quote(String text) {
        return "\"" + text.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }
}
