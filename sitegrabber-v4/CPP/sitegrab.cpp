// language: C++17, file: CPP/sitegrab.cpp
// SiteGrabber v3.0 — Core crawler (WinHTTP, zero external deps)
// Build: cl.exe /std:c++17 /O2 /EHsc /D_WIN32_WINNT=0x0A00 /DUNICODE
//        sitegrab.cpp /Fe:sitegrab.exe winhttp.lib
// Usage: sitegrab.exe <url> <output_dir> [depth=3]

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>
#include <winhttp.h>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <mutex>
#include <queue>
#include <regex>
#include <set>
#include <sstream>
#include <string>
#include <thread>
#include <vector>
#include <condition_variable>
#include <atomic>

namespace fs = std::filesystem;

// ── String helpers (C++17 safe) ────────────────────────────────────────────────
static bool str_ends_with(const std::string& s, const std::string& suffix) {
    return s.size() >= suffix.size() &&
           s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0;
}
static bool str_starts_with(const std::string& s, const std::string& prefix) {
    return s.size() >= prefix.size() &&
           s.compare(0, prefix.size(), prefix) == 0;
}

// ── Narrow/wide conversion ────────────────────────────────────────────────────
static std::wstring to_wide(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    std::wstring w(n - 1, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, w.data(), n);
    return w;
}
static std::string to_narrow(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string s(n - 1, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, s.data(), n, nullptr, nullptr);
    return s;
}

// ── Thread pool ───────────────────────────────────────────────────────────────
class ThreadPool {
public:
    explicit ThreadPool(size_t n) {
        for (size_t i = 0; i < n; ++i)
            workers_.emplace_back([this] { worker_loop(); });
    }
    ~ThreadPool() {
        { std::lock_guard lk(mx_); stop_ = true; }
        cv_.notify_all();
        for (auto& t : workers_) if (t.joinable()) t.join();
    }
    void enqueue(std::function<void()> f) {
        { std::lock_guard lk(mx_); q_.push(std::move(f)); }
        cv_.notify_one();
    }
    void wait_idle() {
        std::unique_lock lk(mx_);
        idle_cv_.wait(lk, [this] { return q_.empty() && active_ == 0; });
    }
private:
    void worker_loop() {
        for (;;) {
            std::function<void()> task;
            {
                std::unique_lock lk(mx_);
                cv_.wait(lk, [this] { return stop_ || !q_.empty(); });
                if (stop_ && q_.empty()) return;
                task = std::move(q_.front()); q_.pop();
                ++active_;
            }
            task();
            {
                std::lock_guard lk(mx_);
                --active_;
            }
            idle_cv_.notify_all();
        }
    }
    std::vector<std::thread>          workers_;
    std::queue<std::function<void()>> q_;
    std::mutex                        mx_;
    std::condition_variable           cv_, idle_cv_;
    bool                              stop_{false};
    int                               active_{0};
};

// ── HTTP GET via WinHTTP ──────────────────────────────────────────────────────
struct HttpResult { int status; std::string body; std::string content_type; };

static HttpResult http_get(const std::wstring& url_w) {
    HttpResult res{0,"",""};

    URL_COMPONENTS uc{};
    uc.dwStructSize      = sizeof(uc);
    wchar_t scheme[16]{}, host[256]{}, path[2048]{};
    uc.lpszScheme        = scheme; uc.dwSchemeLength   = 16;
    uc.lpszHostName      = host;   uc.dwHostNameLength = 256;
    uc.lpszUrlPath       = path;   uc.dwUrlPathLength  = 2048;
    if (!WinHttpCrackUrl(url_w.c_str(), 0, 0, &uc)) return res;

    bool is_https = (uc.nScheme == INTERNET_SCHEME_HTTPS);

    HINTERNET sess = WinHttpOpen(L"SiteGrabber/3.0",
        WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS, 0);
    if (!sess) return res;

    HINTERNET conn = WinHttpConnect(sess, host, uc.nPort, 0);
    if (!conn) { WinHttpCloseHandle(sess); return res; }

    DWORD flags = WINHTTP_FLAG_REFRESH;
    if (is_https) flags |= WINHTTP_FLAG_SECURE;
    HINTERNET req = WinHttpOpenRequest(conn, L"GET", path,
        nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!req) { WinHttpCloseHandle(conn); WinHttpCloseHandle(sess); return res; }

    if (is_https) {
        DWORD sec = SECURITY_FLAG_IGNORE_UNKNOWN_CA |
                    SECURITY_FLAG_IGNORE_CERT_CN_INVALID |
                    SECURITY_FLAG_IGNORE_CERT_DATE_INVALID;
        WinHttpSetOption(req, WINHTTP_OPTION_SECURITY_FLAGS, &sec, sizeof(sec));
    }

    if (!WinHttpSendRequest(req, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(req, nullptr)) {
        WinHttpCloseHandle(req); WinHttpCloseHandle(conn); WinHttpCloseHandle(sess);
        return res;
    }

    // Status code
    DWORD status_sz = sizeof(DWORD), sc = 0;
    WinHttpQueryHeaders(req, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                        nullptr, &sc, &status_sz, nullptr);
    res.status = static_cast<int>(sc);

    // Content-Type
    wchar_t ct_buf[256]{};
    DWORD ct_sz = sizeof(ct_buf);
    WinHttpQueryHeaders(req, WINHTTP_QUERY_CONTENT_TYPE,
                        nullptr, ct_buf, &ct_sz, nullptr);
    res.content_type = to_narrow(ct_buf);

    // Body
    DWORD avail = 0;
    std::string body;
    while (WinHttpQueryDataAvailable(req, &avail) && avail > 0) {
        std::vector<char> buf(avail);
        DWORD read = 0;
        if (WinHttpReadData(req, buf.data(), avail, &read))
            body.append(buf.data(), read);
    }
    res.body = std::move(body);

    WinHttpCloseHandle(req);
    WinHttpCloseHandle(conn);
    WinHttpCloseHandle(sess);
    return res;
}

// ── URL utils ─────────────────────────────────────────────────────────────────
static std::string url_to_path(const std::string& url, const std::string& out_dir) {
    URL_COMPONENTS uc{};
    uc.dwStructSize = sizeof(uc);
    wchar_t path_w[2048]{};
    uc.lpszUrlPath = path_w; uc.dwUrlPathLength = 2048;
    std::wstring wu = to_wide(url);
    WinHttpCrackUrl(wu.c_str(), 0, 0, &uc);

    std::string p = to_narrow(path_w);
    if (p.empty() || str_ends_with(p, "/")) p += "index.html";
    if (!str_ends_with(p, ".html") && !str_ends_with(p, ".htm") &&
        !str_ends_with(p, ".css") && !str_ends_with(p, ".js")  &&
        !str_ends_with(p, ".json")&& !str_ends_with(p, ".xml") &&
        !str_ends_with(p, ".txt") && !str_ends_with(p, ".svg") &&
        !str_ends_with(p, ".png") && !str_ends_with(p, ".jpg") &&
        !str_ends_with(p, ".gif") && !str_ends_with(p, ".ico") &&
        !str_ends_with(p, ".webp"))
        p += ".html";

    // Replace / with OS separator, strip leading slash
    if (!p.empty() && p[0] == '/') p = p.substr(1);
    for (char& c : p)
        if (c == '/' || c == '\\') c = fs::path::preferred_separator;

    return (fs::path(out_dir) / p).string();
}

static std::string extract_host(const std::string& url) {
    auto s = url.find("://");
    if (s == std::string::npos) return "";
    auto start = s + 3;
    auto end = url.find('/', start);
    return url.substr(start, end == std::string::npos ? std::string::npos : end - start);
}

// ── Link extraction ────────────────────────────────────────────────────────────
static std::vector<std::string> extract_links(
    const std::string& html,
    const std::string& base_url,
    const std::string& host)
{
    std::vector<std::string> links;
    // href / src / action
    static const std::regex rx(
        R"((?:href|src|action)\s*=\s*["']([^"'#>]{4,})["'])",
        std::regex::icase);
    for (auto it = std::sregex_iterator(html.begin(), html.end(), rx);
         it != std::sregex_iterator(); ++it)
    {
        std::string raw = (*it)[1].str();
        // Skip anchors, mailto, javascript, data URIs
        if (str_starts_with(raw,"#") || str_starts_with(raw,"mailto:") ||
            str_starts_with(raw,"javascript:") || str_starts_with(raw,"data:"))
            continue;
        // Relative → absolute
        if (!str_starts_with(raw,"http")) {
            if (!raw.empty() && raw[0] == '/') {
                auto proto_end = base_url.find("//");
                auto slash = base_url.find('/', proto_end + 2);
                raw = base_url.substr(0, slash == std::string::npos ? std::string::npos : slash) + raw;
            } else {
                auto slash = base_url.rfind('/');
                raw = (slash != std::string::npos ? base_url.substr(0, slash + 1) : base_url) + raw;
            }
        }
        // Same-host only
        if (extract_host(raw) == host)
            links.push_back(raw);
    }
    return links;
}

// ── Crawler ────────────────────────────────────────────────────────────────────
class Crawler {
public:
    Crawler(const std::string& start, const std::string& out, int depth, int threads)
        : start_(start), out_(out), max_depth_(depth), pool_(threads) {}

    void run() {
        std::string host = extract_host(start_);
        queue_.push({start_, 0});

        std::atomic<bool> running{true};
        while (running || !queue_.empty()) {
            std::string url;
            int depth = 0;
            {
                std::lock_guard lk(queue_mx_);
                if (queue_.empty()) { running = false; break; }
                auto [u, d] = queue_.front(); queue_.pop();
                url = u; depth = d;
            }
            {
                std::lock_guard lk(vis_mx_);
                if (!visited_.insert(url).second) continue;
            }

            pool_.enqueue([=, this]() {
                crawl_one(url, depth, host);
            });

            pool_.wait_idle();  // BFS gate
        }
        pool_.wait_idle();
        std::cout << "[INFO] Done. URLs=" << urls_crawled_ << " Files=" << files_saved_ << "\n";
    }

private:
    void crawl_one(const std::string& url, int depth, const std::string& host) {
        auto res = http_get(to_wide(url));
        if (res.status == 0) { std::cout << "[ERR] " << url << "\n"; return; }

        std::cout << "[" << res.status << "] " << url << "\n";
        ++urls_crawled_;

        // Save
        auto local = url_to_path(url, out_);
        try {
            fs::create_directories(fs::path(local).parent_path());
            std::ofstream f(local, std::ios::binary);
            f.write(res.body.c_str(), res.body.size());
            ++files_saved_;
            std::cout << "[SAVE] " << local << "\n";
        } catch(...) {}

        // Follow links
        if (depth < max_depth_ &&
            (res.content_type.find("html") != std::string::npos ||
             res.content_type.empty()))
        {
            auto links = extract_links(res.body, url, host);
            std::lock_guard lk(queue_mx_);
            for (auto& l : links) {
                std::lock_guard vl(vis_mx_);
                if (visited_.find(l) == visited_.end())
                    queue_.push({l, depth + 1});
            }
        }
    }

    std::string start_, out_;
    int         max_depth_;
    ThreadPool  pool_;

    std::queue<std::pair<std::string,int>> queue_;
    std::mutex                             queue_mx_;
    std::set<std::string>                  visited_;
    std::mutex                             vis_mx_;
    std::atomic<int>                       urls_crawled_{0}, files_saved_{0};
};

// ── main ──────────────────────────────────────────────────────────────────────
int wmain(int argc, wchar_t* argv[]) {
    if (argc < 3) {
        std::wcerr << L"Usage: sitegrab.exe <url> <output_dir> [depth=3]\n";
        return 1;
    }
    std::string url  = to_narrow(argv[1]);
    std::string out  = to_narrow(argv[2]);
    int         dep  = argc >= 4 ? std::stoi(to_narrow(argv[3])) : 3;

    std::cout << "[INFO] Target: " << url << "\n";
    std::cout << "[INFO] Output: " << out << "\n";
    std::cout << "[INFO] Depth:  " << dep << "\n";

    fs::create_directories(out);

    Crawler c(url, out, dep, 8);
    c.run();
    return 0;
}
