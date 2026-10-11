import Foundation
import Vision
import ImageIO

// Verify visible controls rather than treating a running, blank app as success.
let url = URL(fileURLWithPath: CommandLine.arguments[1])
let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
try VNImageRequestHandler(url: url).perform([request])
let text = (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
print(text)
let lower = text.lowercased()
guard lower.contains("e-rate lookup"), lower.contains("search ca"), lower.contains("look up") else {
    fputs("Lookup controls are not visible in the simulator screenshot.\n", stderr)
    exit(1)
}
