// language: C++17, file: sitegrab.cpp, target: Windows 10/11, MSVC cl.exe
// NO external dependencies — uses WinHTTP (built into Windows)
// build: cl.exe /std:c++17 /O2 /EHsc /D_WIN32_WINNT=0x0A00 /DUNICODE sitegrab.cpp /Fe:sitegrab.exe winhttp.lib
// or: build.bat

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif

#include <windows.h>
#include <winhttp.h>
#pragma comment(lib, "winhttp.lib")

#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>
#include <deque>
#include <queue>
#include <unordered_set>
#include <unordered_map>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <functional>
#include <regex>
#include <algorithm>
#include <chrono>
#include <cstring>

namespace fs = std::filesystem;
using namespace std::chrono_literals;

// ── C++17-safe string helpers (no C++20 ends_with / starts_with) ────────────
static bool str_ends_with(const std::string& s, const std::string& suffix) {
    return s.size() >= suffix.size() &&
           s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0;
}
static bool str_starts_with(const std::string& s, const std::string& prefix) {
    return s.size() >= prefix.size() && s.compare(0, prefix.size(), prefix) == 0;
}
static std::wstring to_wide(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    std::wstring out(n, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, out.data(), n);
    out.resize(n - 1);
    return out;
}
static std::string to_narrow(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string out(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, out.data(), n, nullptr, nullptr);
    out.resize(n - 1);
    return out;
}

// ── regex patterns ────────────────────────────────────────────────────────────
static const std::regex RE_CSS_URL(
    R"(url\([\"']?(?!data:)([^\"'\)\s]+)[\"']?\))",
    std::regex::icase | std::regex::optimize);

static const std::regex RE_SRC_HREF(
    R"((?:src|href|data-src|data-original|data-lazy-src|poster)\s*=\s*[\"']([^\"'\s>][^\"']*)[\"'])",
    std::regex::icase | std::regex::optimize);

static const std::regex RE_A_HREF(
    R"(<a[^>]+?href\s*=\s*[\"']([^\"'#][^\"']*)[\"'])",
    std::regex::icase | std::regex::optimize);

// ── WinHTTP wrapper ───────────────────────────────────────────────────────────
static HINTERNET g_session = nullptr;

bool http_init() {
    g_session = WinHttpOpen(
        L"SiteGrabber/2.0",
        WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS, 0);
    return g_session != nullptr;
}
void http_cleanup() {
    if (g_session) { WinHttpCloseHandle(g_session); g_session = nullptr; }
}

struct Response {
    std::vector<char> body;
    std::string       content_type;
    DWORD             status = 0;
};

Response http_get(const std::string& url_str) {
    Response res;
    if (!g_session) return res;

    std::wstring wurl = to_wide(url_str);

    URL_COMPONENTS comp   = {};
    comp.dwStructSize     = sizeof(comp);
    wchar_t host[512]={}, path[4096]={}, extra[2048]={};
    comp.lpszHostName     = host;  comp.dwHostNameLength  = 512;
    comp.lpszUrlPath      = path;  comp.dwUrlPathLength   = 4096;
    comp.lpszExtraInfo    = extra; comp.dwExtraInfoLength = 2048;

    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &comp)) return res;

    bool   is_https = (comp.nScheme == INTERNET_SCHEME_HTTPS);
    DWORD  flags    = is_https ? WINHTTP_FLAG_SECURE : 0;

    HINTERNET hConn = WinHttpConnect(g_session, host, comp.nPort, 0);
    if (!hConn) return res;

    std::wstring full_path = std::wstring(path) + extra;
    HINTERNET hReq = WinHttpOpenRequest(hConn, L"GET",
        full_path.empty() ? L"/" : full_path.c_str(),
        nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { WinHttpCloseHandle(hConn); return res; }

    // allow self-signed certs
    if (is_https) {
        DWORD sec = SECURITY_FLAG_IGNORE_ALL_CERT_ERRORS;
        WinHttpSetOption(hReq, WINHTTP_OPTION_SECURITY_FLAGS, &sec, sizeof(sec));
    }

    WinHttpAddRequestHeaders(hReq,
        L"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n"
        L"Accept-Language: en-US,en;q=0.5\r\n"
        L"Connection: keep-alive\r\n",
        (DWORD)-1, WINHTTP_ADDREQ_FLAG_ADD);

    if (!WinHttpSendRequest(hReq, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(hReq, nullptr)) {
        WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConn);
        return res;
    }

    // status
    DWORD sc = 0, sz = sizeof(sc);
    WinHttpQueryHeaders(hReq,
        WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        nullptr, &sc, &sz, nullptr);
    res.status = sc;

    // content-type
    DWORD ctSz = 0;
    WinHttpQueryHeaders(hReq, WINHTTP_QUERY_CONTENT_TYPE,
        nullptr, nullptr, &ctSz, nullptr);
    if (GetLastError() == ERROR_INSUFFICIENT_BUFFER && ctSz > 0) {
        std::wstring wct(ctSz / sizeof(wchar_t) + 1, 0);
        WinHttpQueryHeaders(hReq, WINHTTP_QUERY_CONTENT_TYPE,
            nullptr, wct.data(), &ctSz, nullptr);
        res.content_type = to_narrow(wct);
        auto sc2 = res.content_type.find(';');
        if (sc2 != std::string::npos) res.content_type.resize(sc2);
    }

    // body
    DWORD avail = 0;
    while (WinHttpQueryDataAvailable(hReq, &avail) && avail > 0) {
        size_t off = res.body.size();
        res.body.resize(off + avail);
        DWORD read = 0;
        WinHttpReadData(hReq, res.body.data() + off, avail, &read);
        res.body.resize(off + read);
    }

    WinHttpCloseHandle(hReq);
    WinHttpCloseHandle(hConn);
    return res;
}

// ── URL utilities ─────────────────────────────────────────────────────────────
struct ParsedUrl { std::string scheme, host, path, query; };

ParsedUrl parse_url(const std::string& url) {
    ParsedUrl r;
    static const std::regex re(
        R"(^(https?)://([^/?#:]+(?::\d+)?)([^?#]*)(\?[^#]*)?(#.*)?$)",
        std::regex::optimize);
    std::smatch m;
    if (std::regex_match(url, m, re)) {
        r.scheme = m[1]; r.host = m[2]; r.path = m[3]; r.query = m[4];
        if (r.path.empty()) r.path = "/";
    }
    return r;
}

std::string join_url(const std::string& base, const std::string& rel) {
    if (rel.empty()) return "";
    // strip fragment from rel
    std::string r = rel;
    auto hash = r.find('#');
    if (hash != std::string::npos) r.resize(hash);
    if (r.empty()) return "";

    if (str_starts_with(r, "http://") || str_starts_with(r, "https://"))
        return r;
    auto b = parse_url(base);
    if (b.scheme.empty()) return "";
    if (r[0] == '/')
        return b.scheme + "://" + b.host + r;
    // relative
    std::string dir = b.path;
    auto sl = dir.rfind('/');
    if (sl != std::string::npos) dir.resize(sl + 1); else dir = "/";
    return b.scheme + "://" + b.host + dir + r;
}

std::string url_to_local(const std::string& url, const std::string& root) {
    auto p = parse_url(url);
    if (p.host.empty()) return "";
    std::string rel = p.path;
    if (rel.empty() || rel.back() == '/') rel += "index.html";
    // no extension → .html
    auto dot = rel.rfind('.');
    auto sl  = rel.rfind('/');
    if (dot == std::string::npos || (sl != std::string::npos && dot < sl))
        rel += ".html";
    // query → safe suffix
    if (!p.query.empty()) {
        std::string q = p.query.substr(1, 60);
        for (auto& c : q)
            if (!isalnum(static_cast<unsigned char>(c)) && c != '=' && c != '&')
                c = '_';
        auto ext = rel.rfind('.');
        rel = rel.substr(0, ext) + "_" + q + rel.substr(ext);
    }
    // sanitize colons for Windows paths
    for (auto& c : rel) if (c == ':') c = '_';
    // build path
    std::string out = root;
    if (out.back() != '/' && out.back() != '\\') out += '/';
    out += p.host;
    out += '/';
    out += rel;
    return out;
}

bool same_domain(const std::string& url, const std::string& host) {
    auto h = parse_url(url).host;
    return h == host || (h.size() > host.size() &&
           str_ends_with(h, "." + host));
}

// ── thread pool ───────────────────────────────────────────────────────────────
class ThreadPool {
    std::vector<std::thread>          workers_;
    std::queue<std::function<void()>> tasks_;
    std::mutex                        mtx_;
    std::condition_variable           cv_, done_cv_;
    std::atomic<int>                  active_{ 0 };
    bool                              stop_  = false;
public:
    explicit ThreadPool(int n) {
        for (int i = 0; i < n; ++i)
            workers_.emplace_back([this] {
                for (;;) {
                    std::function<void()> task;
                    {
                        std::unique_lock<std::mutex> lk(mtx_);
                        cv_.wait(lk, [&]{ return stop_ || !tasks_.empty(); });
                        if (stop_ && tasks_.empty()) return;
                        task = std::move(tasks_.front());
                        tasks_.pop();
                    }
                    ++active_;
                    task();
                    --active_;
                    done_cv_.notify_all();
                }
            });
    }
    ~ThreadPool() {
        { std::lock_guard<std::mutex> lk(mtx_); stop_ = true; }
        cv_.notify_all();
        for (auto& w : workers_) w.join();
    }
    template<typename F>
    void submit(F&& f) {
        { std::lock_guard<std::mutex> lk(mtx_); tasks_.emplace(std::forward<F>(f)); }
        cv_.notify_one();
    }
    void wait_all() {
        std::unique_lock<std::mutex> lk(mtx_);
        done_cv_.wait(lk, [&]{ return tasks_.empty() && active_ == 0; });
    }
};

// ── file save ─────────────────────────────────────────────────────────────────
bool save_file(const std::string& path, const std::vector<char>& data) {
    try {
        fs::create_directories(fs::path(path).parent_path());
        std::ofstream f(path, std::ios::binary);
        if (!f) return false;
        f.write(data.data(), static_cast<std::streamsize>(data.size()));
        return true;
    } catch (...) { return false; }
}

// ── stats ─────────────────────────────────────────────────────────────────────
struct Stats {
    std::atomic<int> pages{0}, assets{0}, errors{0};
};

// ── crawler ───────────────────────────────────────────────────────────────────
struct CrawlConfig {
    std::string start_url;
    std::string output_dir = "grabbed";
    int         max_depth  = 4;
    int         threads    = 16;
};

class Crawler {
    CrawlConfig  cfg_;
    std::string  base_host_;
    Stats        stats_;

    std::unordered_set<std::string> visited_;
    std::unordered_set<std::string> queued_assets_;
    std::vector<std::pair<std::string,std::string>> css_queue_; // <local,url>
    std::mutex mtx_visited_, mtx_assets_, mtx_css_;

    static std::string body_str(const Response& r) {
        return std::string(r.body.begin(), r.body.end());
    }

    std::string normalise(const std::string& url) const {
        std::string u = url;
        if (!u.empty() && u.back() == '/') u.pop_back();
        // lowercase scheme+host only
        auto after_scheme = u.find("://");
        if (after_scheme != std::string::npos) {
            for (size_t i = 0; i < after_scheme + 3; ++i)
                u[i] = static_cast<char>(tolower(static_cast<unsigned char>(u[i])));
            auto path_start = u.find('/', after_scheme + 3);
            if (path_start == std::string::npos) path_start = u.size();
            for (size_t i = after_scheme + 3; i < path_start; ++i)
                u[i] = static_cast<char>(tolower(static_cast<unsigned char>(u[i])));
        }
        return u;
    }

    std::vector<std::string> extract_assets(const std::string& html,
                                             const std::string& page_url) {
        std::vector<std::string> out;
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_SRC_HREF);
             it != std::sregex_iterator(); ++it) {
            auto raw = (*it)[1].str();
            if (str_starts_with(raw, "data:")) continue;
            auto u = join_url(page_url, raw);
            if (!u.empty()) out.push_back(u);
        }
        // inline style url()
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_CSS_URL);
             it != std::sregex_iterator(); ++it) {
            auto u = join_url(page_url, (*it)[1].str());
            if (!u.empty()) out.push_back(u);
        }
        return out;
    }

    std::vector<std::string> extract_links(const std::string& html,
                                            const std::string& page_url) {
        std::vector<std::string> out;
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_A_HREF);
             it != std::sregex_iterator(); ++it) {
            auto u = join_url(page_url, (*it)[1].str());
            if (!u.empty() && same_domain(u, base_host_))
                out.push_back(u);
        }
        return out;
    }

    void download_asset(const std::string& url) {
        auto local = url_to_local(url, cfg_.output_dir);
        if (local.empty()) return;
        if (fs::exists(local)) return;

        auto res = http_get(url);
        if (res.status == 200 && !res.body.empty()) {
            save_file(local, res.body);
            ++stats_.assets;
            std::cout << "  [asset] " << url << "\n";
            bool is_css = res.content_type.find("css") != std::string::npos
                       || str_ends_with(local, ".css");
            if (is_css) {
                std::lock_guard<std::mutex> lk(mtx_css_);
                css_queue_.emplace_back(local, url);
            }
        } else {
            ++stats_.errors;
            std::cerr << "  [ERR " << res.status << "] " << url << "\n";
        }
    }

public:
    void run(const CrawlConfig& cfg) {
        cfg_       = cfg;
        base_host_ = parse_url(cfg_.start_url).host;

        auto t0 = std::chrono::steady_clock::now();

        std::cout << "\n[sitegrab] target  -> " << cfg_.start_url  << "\n"
                  << "[sitegrab] depth   -> " << cfg_.max_depth   << "\n"
                  << "[sitegrab] threads -> " << cfg_.threads     << "\n"
                  << "[sitegrab] output  -> " << cfg_.output_dir
                                              << "/" << base_host_ << "/\n\n";

        // ── BFS crawl ─────────────────────────────────────────────────────────
        std::deque<std::pair<std::string,int>> page_queue;
        page_queue.push_back({ cfg_.start_url, 0 });

        ThreadPool pool(cfg_.threads);

        while (!page_queue.empty()) {
            auto [url, depth] = page_queue.front();
            page_queue.pop_front();

            std::string nurl = normalise(url);
            {
                std::lock_guard<std::mutex> lk(mtx_visited_);
                if (!visited_.insert(nurl).second) continue;
            }

            auto local = url_to_local(url, cfg_.output_dir);
            if (local.empty()) continue;

            auto res = http_get(url);
            if (res.status != 200 || res.body.empty()) {
                ++stats_.errors;
                std::cerr << "  [ERR " << res.status << "] " << url << "\n";
                continue;
            }
            save_file(local, res.body);
            ++stats_.pages;
            std::cout << "  [page] " << url << "\n";

            std::string html = body_str(res);

            // queue assets
            for (auto& a : extract_assets(html, url)) {
                std::lock_guard<std::mutex> lk(mtx_assets_);
                if (queued_assets_.insert(normalise(a)).second) {
                    pool.submit([this, a]{ download_asset(a); });
                }
            }

            // enqueue child pages
            if (depth < cfg_.max_depth) {
                for (auto& lnk : extract_links(html, url)) {
                    std::lock_guard<std::mutex> lk(mtx_visited_);
                    if (!visited_.count(normalise(lnk)))
                        page_queue.push_back({ lnk, depth + 1 });
                }
            }
        }

        pool.wait_all();

        // ── CSS sweep for fonts + background images ───────────────────────────
        std::vector<std::pair<std::string,std::string>> css_snap;
        { std::lock_guard<std::mutex> lk(mtx_css_); css_snap = css_queue_; }

        std::unordered_set<std::string> wave2;
        for (auto& [css_local, css_url] : css_snap) {
            try {
                std::ifstream f(css_local);
                std::string text((std::istreambuf_iterator<char>(f)),
                                  std::istreambuf_iterator<char>());
                for (auto it = std::sregex_iterator(text.begin(), text.end(), RE_CSS_URL);
                     it != std::sregex_iterator(); ++it) {
                    auto u = join_url(css_url, (*it)[1].str());
                    if (!u.empty()) wave2.insert(u);
                }
            } catch (...) {}
        }

        if (!wave2.empty()) {
            std::cout << "\n[sitegrab] CSS sweep -> " << wave2.size() << " more assets\n\n";
            ThreadPool pool2(cfg_.threads);
            for (auto& u : wave2) {
                std::lock_guard<std::mutex> lk(mtx_assets_);
                if (queued_assets_.insert(normalise(u)).second)
                    pool2.submit([this, u]{ download_asset(u); });
            }
            pool2.wait_all();
        }

        auto sec = std::chrono::duration_cast<std::chrono::seconds>(
            std::chrono::steady_clock::now() - t0).count();

        std::cout << "\n" << std::string(60,'-') << "\n"
                  << "[sitegrab] pages   : " << stats_.pages  << "\n"
                  << "[sitegrab] assets  : " << stats_.assets << "\n"
                  << "[sitegrab] errors  : " << stats_.errors << "\n"
                  << "[sitegrab] elapsed : " << sec           << "s\n"
                  << "[sitegrab] output  : "
                  << cfg_.output_dir << "/" << base_host_ << "/\n"
                  << std::string(60,'-') << "\n";
    }
};

// ── main ──────────────────────────────────────────────────────────────────────
int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cout << "SiteGrabber v2.0 (WinHTTP, no external deps)\n\n"
                  << "usage: sitegrab <url> [output_dir] [depth] [threads]\n\n"
                  << "examples:\n"
                  << "  sitegrab https://example.com\n"
                  << "  sitegrab https://shop.example.com grabbed 4 16\n";
        return 1;
    }

    if (!http_init()) {
        std::cerr << "[fatal] WinHttpOpen failed (" << GetLastError() << ")\n";
        return 1;
    }

    CrawlConfig cfg;
    cfg.start_url  = argv[1];
    if (argc > 2) cfg.output_dir = argv[2];
    if (argc > 3) cfg.max_depth  = std::stoi(argv[3]);
    if (argc > 4) cfg.threads    = std::stoi(argv[4]);

    Crawler c;
    c.run(cfg);

    http_cleanup();
    return 0;
}
