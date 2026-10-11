// language: C++17, file: sitegrab.cpp, target: Windows 10/11, MSVC cl.exe
// SiteGrabber v3 — pagination exhaustion, sitemap, product manifest, resume, CSS sweep
// build: cl.exe /std:c++17 /O2 /EHsc /W3 /D_WIN32_WINNT=0x0A00 /DUNICODE /D_UNICODE sitegrab.cpp /Fe:sitegrab.exe winhttp.lib

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

#include <string>
#include <vector>
#include <deque>
#include <set>
#include <queue>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <functional>
#include <regex>
#include <chrono>
#include <cctype>
#include <fstream>
#include <sstream>
#include <iostream>

using namespace std::chrono_literals;

// ── string helpers ──────────────────────────────────────────────────────────
static bool sw(const std::string& s, const std::string& p) { return s.rfind(p, 0) == 0; }
static bool ew(const std::string& s, const std::string& p) {
    return s.size() >= p.size() && s.compare(s.size() - p.size(), p.size(), p) == 0;
}
static std::string trim(const std::string& s) {
    size_t a = s.find_first_not_of(" \t\r\n");
    if (a == std::string::npos) return "";
    size_t b = s.find_last_not_of(" \t\r\n");
    return s.substr(a, b - a + 1);
}
static std::wstring to_w(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    if (n <= 0) return {};
    std::wstring w((size_t)n, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &w[0], n);
    w.resize((size_t)n - 1);
    return w;
}
static std::string to_n(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (n <= 0) return {};
    std::string s((size_t)n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), -1, &s[0], n, nullptr, nullptr);
    s.resize((size_t)n - 1);
    return s;
}

// ── Win32 file helpers (no <filesystem>) ─────────────────────────────────────
static bool file_exists(const std::string& path) {
    DWORD a = GetFileAttributesW(to_w(path).c_str());
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

static void make_dirs(const std::string& path) {
    if (path.empty()) return;
    std::string p = path;
    for (auto& c : p) if (c == '/') c = '\\';
    std::string cur;
    cur.reserve(p.size());
    for (size_t i = 0; i < p.size(); ++i) {
        cur.push_back(p[i]);
        if (p[i] == '\\' && cur.size() > 3)
            CreateDirectoryW(to_w(cur).c_str(), nullptr);
    }
    CreateDirectoryW(to_w(cur).c_str(), nullptr);
}

static void make_parent(const std::string& path) {
    auto sl = path.find_last_of("/\\");
    if (sl == std::string::npos) return;
    make_dirs(path.substr(0, sl));
}

static bool write_binary(const std::string& path, const std::vector<char>& data) {
    make_parent(path);
    std::ofstream f(to_w(path).c_str(), std::ios::binary | std::ios::trunc);
    if (!f) return false;
    if (!data.empty()) f.write(data.data(), (std::streamsize)data.size());
    return (bool)f;
}

static bool read_text(const std::string& path, std::string& out) {
    std::ifstream f(to_w(path).c_str(), std::ios::binary);
    if (!f) return false;
    std::stringstream ss; ss << f.rdbuf();
    out = ss.str();
    return true;
}

static void walk_css(const std::string& root, std::vector<std::string>& out) {
    std::string pattern = root;
    if (!pattern.empty() && pattern.back() != '\\' && pattern.back() != '/')
        pattern += "\\";
    pattern += "*";

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(to_w(pattern).c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;

    do {
        std::wstring name = fd.cFileName;
        if (name == L"." || name == L"..") continue;

        std::string full = root;
        if (!full.empty() && full.back() != '\\' && full.back() != '/') full += "\\";
        full += to_n(name);

        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            walk_css(full, out);
        } else {
            if (name.size() >= 4) {
                std::wstring tail = name.substr(name.size() - 4);
                if (tail == L".css" || tail == L".CSS")
                    out.push_back(full);
            }
        }
    } while (FindNextFileW(h, &fd));

    FindClose(h);
}

// ── regex ────────────────────────────────────────────────────────────────────
static const std::regex RE_CSS_URL(
    R"(url\([\"']?(?!data:)([^\"'\)\s]+)[\"']?\))",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_SRC_HREF(
    R"((?:src|href|data-src|data-original|data-lazy-src|poster|data)\s*=\s*[\"']([^\"'\s>][^\"']*)[\"'])",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_SRCSET(
    R"(srcset\s*=\s*[\"']([^\"']+)[\"'])",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_A_HREF(
    R"(<a[^>]+?href\s*=\s*[\"']([^\"'#][^\"']*)[\"'])",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_FORM_ACTION(
    R"(<form[^>]+?action\s*=\s*[\"']([^\"']+)[\"'])",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_JSONLD(
    R"(<script[^>]+type\s*=\s*[\"']application/ld\+json[\"'][^>]*>([\s\S]*?)</script>)",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_OG_META(
    R"(<meta[^>]+(?:property|name)\s*=\s*[\"'](og:[^\"']+|product:[^\"']+)[\"'][^>]*content\s*=\s*[\"']([^\"']*)[\"'])",
    std::regex::icase | std::regex::optimize);
static const std::regex RE_LOC(
    R"(<loc>\s*([^<\s]+)\s*</loc>)",
    std::regex::icase | std::regex::optimize);

// ── winhttp ─────────────────────────────────────────────────────────────────
static HINTERNET g_sess = nullptr;

static bool http_init() {
    g_sess = WinHttpOpen(L"SiteGrabber/3.0",
                         WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                         WINHTTP_NO_PROXY_NAME,
                         WINHTTP_NO_PROXY_BYPASS, 0);
    return g_sess != nullptr;
}
static void http_cleanup() {
    if (g_sess) { WinHttpCloseHandle(g_sess); g_sess = nullptr; }
}

struct Response {
    std::vector<char> body;
    std::string       ctype;
    DWORD             status = 0;
};

static Response http_get(const std::string& url_str) {
    Response res;
    if (!g_sess) return res;

    std::wstring wurl = to_w(url_str);

    URL_COMPONENTS c;
    ZeroMemory(&c, sizeof(c));
    c.dwStructSize      = sizeof(c);
    wchar_t host[512]   = { 0 };
    wchar_t path[4096]  = { 0 };
    wchar_t extra[2048] = { 0 };
    c.lpszHostName      = host;  c.dwHostNameLength  = 512;
    c.lpszUrlPath       = path;  c.dwUrlPathLength   = 4096;
    c.lpszExtraInfo     = extra; c.dwExtraInfoLength = 2048;

    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &c)) return res;

    bool  https = (c.nScheme == INTERNET_SCHEME_HTTPS);
    DWORD flags = https ? WINHTTP_FLAG_SECURE : 0;

    HINTERNET hconn = WinHttpConnect(g_sess, host, c.nPort, 0);
    if (!hconn) return res;

    std::wstring full = std::wstring(path) + extra;
    HINTERNET hreq = WinHttpOpenRequest(hconn, L"GET",
        full.empty() ? L"/" : full.c_str(),
        nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hreq) { WinHttpCloseHandle(hconn); return res; }

    if (https) {
        DWORD sec = SECURITY_FLAG_IGNORE_ALL_CERT_ERRORS;
        WinHttpSetOption(hreq, WINHTTP_OPTION_SECURITY_FLAGS, &sec, sizeof(sec));
    }

    WinHttpAddRequestHeaders(hreq,
        L"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\r\n"
        L"Accept-Language: en-US,en;q=0.5\r\n"
        L"User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        L"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36\r\n"
        L"Connection: keep-alive\r\n",
        (DWORD)-1, WINHTTP_ADDREQ_FLAG_ADD);

    if (!WinHttpSendRequest(hreq, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(hreq, nullptr)) {
        WinHttpCloseHandle(hreq);
        WinHttpCloseHandle(hconn);
        return res;
    }

    DWORD sc = 0, sz = sizeof(sc);
    WinHttpQueryHeaders(hreq,
        WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
        nullptr, &sc, &sz, nullptr);
    res.status = sc;

    DWORD ctsz = 0;
    WinHttpQueryHeaders(hreq, WINHTTP_QUERY_CONTENT_TYPE,
        nullptr, nullptr, &ctsz, nullptr);
    if (GetLastError() == ERROR_INSUFFICIENT_BUFFER && ctsz > 0) {
        std::wstring wct((size_t)ctsz / sizeof(wchar_t) + 1, 0);
        WinHttpQueryHeaders(hreq, WINHTTP_QUERY_CONTENT_TYPE,
            nullptr, &wct[0], &ctsz, nullptr);
        res.ctype = to_n(wct);
        auto sc2 = res.ctype.find(';');
        if (sc2 != std::string::npos) res.ctype.resize(sc2);
    }

    DWORD avail = 0;
    while (WinHttpQueryDataAvailable(hreq, &avail) && avail > 0) {
        size_t off = res.body.size();
        res.body.resize(off + avail);
        DWORD read = 0;
        WinHttpReadData(hreq, &res.body[off], avail, &read);
        res.body.resize(off + read);
    }

    WinHttpCloseHandle(hreq);
    WinHttpCloseHandle(hconn);
    return res;
}

// ── url parsing ──────────────────────────────────────────────────────────────
struct ParsedUrl { std::string scheme, host, path, query; };

static ParsedUrl parse_url(const std::string& url) {
    ParsedUrl r;
    static const std::regex re(
        R"(^(https?)://([^/?#:]+(?::\d+)?)([^?#]*)(\?[^#]*)?)",
        std::regex::optimize);
    std::smatch m;
    if (std::regex_match(url, m, re)) {
        r.scheme = m[1]; r.host = m[2]; r.path = m[3]; r.query = m[4];
        if (r.path.empty()) r.path = "/";
    }
    return r;
}

static std::string join_url(const std::string& base, const std::string& rel) {
    if (rel.empty()) return "";
    std::string r = rel;
    auto h = r.find('#');
    if (h != std::string::npos) r.resize(h);
    if (r.empty()) return "";
    if (sw(r, "http://") || sw(r, "https://")) return r;

    auto b = parse_url(base);
    if (b.scheme.empty()) return "";

    if (r[0] == '/') return b.scheme + "://" + b.host + r;

    std::string dir = b.path;
    auto sl = dir.rfind('/');
    if (sl != std::string::npos) dir.resize(sl + 1); else dir = "/";
    return b.scheme + "://" + b.host + dir + r;
}

static bool same_domain(const std::string& url, const std::string& host) {
    auto h = parse_url(url).host;
    return h == host || (h.size() > host.size() && ew(h, "." + host));
}

static std::string sanitize_q(const std::string& q) {
    std::string out;
    out.reserve(q.size());
    for (char ch : q) {
        if (isalnum((unsigned char)ch) || ch == '=' || ch == '&' || ch == '-')
            out += ch;
        else
            out += '_';
    }
    if (out.size() > 80) out.resize(80);
    return out;
}

static std::string url_to_local(const std::string& url, const std::string& root) {
    auto p = parse_url(url);
    if (p.host.empty()) return "";

    std::string rel = p.path.empty() ? "index.html" : p.path;
    if (!rel.empty() && rel.back() == '/') rel += "index.html";

    auto dot = rel.rfind('.');
    auto sl  = rel.rfind('/');
    if (dot == std::string::npos || (sl != std::string::npos && dot < sl))
        rel += ".html";

    if (!p.query.empty()) {
        std::string q = sanitize_q(p.query.substr(1));
        auto ext = rel.rfind('.');
        rel = rel.substr(0, ext) + "_" + q + rel.substr(ext);
    }

    for (auto& ch : rel) {
        if (ch == ':' || ch == '<' || ch == '>' || ch == '"' ||
            ch == '|' || ch == '?' || ch == '*')
            ch = '_';
    }

    std::string out = root;
    if (!out.empty() && out.back() != '/' && out.back() != '\\') out += '/';
    out += p.host + "/" + rel;
    return out;
}

// ── pagination expander ──────────────────────────────────────────────────────
static const std::vector<std::string> PAGE_KEYS = {
    "page", "p", "pg", "offset", "start", "skip", "from", "pageno", "page_num"
};

static std::vector<std::string> expand_pagination(const std::string& url, int ceiling = 500) {
    std::vector<std::string> out;
    auto qpos = url.find('?');
    if (qpos == std::string::npos) return out;

    std::string base = url.substr(0, qpos);
    std::string qs   = url.substr(qpos + 1);

    std::vector<std::pair<std::string, std::string>> kv;
    std::stringstream ss(qs);
    std::string pair;
    while (std::getline(ss, pair, '&')) {
        auto eq = pair.find('=');
        if (eq == std::string::npos) kv.push_back({ pair, "" });
        else kv.push_back({ pair.substr(0, eq), pair.substr(eq + 1) });
    }

    for (const auto& key : PAGE_KEYS) {
        for (size_t i = 0; i < kv.size(); ++i) {
            if (kv[i].first != key) continue;
            int cur = 0;
            try { cur = std::stoi(kv[i].second); } catch (...) { continue; }
            int step = (key == "offset" || key == "start" || key == "skip") ? 10 : 1;
            for (int n = cur + step; n <= cur + step * ceiling; n += step) {
                std::string q;
                for (size_t j = 0; j < kv.size(); ++j) {
                    if (j) q += "&";
                    q += kv[j].first + "=" + (j == i ? std::to_string(n) : kv[j].second);
                }
                out.push_back(base + "?" + q);
            }
            return out;
        }
    }
    return out;
}

// ── thread pool ──────────────────────────────────────────────────────────────
class Pool {
    std::vector<std::thread>          workers_;
    std::queue<std::function<void()>> q_;
    std::mutex                        m_;
    std::condition_variable           cv_, done_;
    std::atomic<int>                  active_{ 0 };
    bool                              stop_ = false;

public:
    explicit Pool(int n) {
        for (int i = 0; i < n; ++i) {
            workers_.emplace_back([this] {
                for (;;) {
                    std::function<void()> t;
                    {
                        std::unique_lock<std::mutex> lk(m_);
                        cv_.wait(lk, [&] { return stop_ || !q_.empty(); });
                        if (stop_ && q_.empty()) return;
                        t = std::move(q_.front());
                        q_.pop();
                    }
                    ++active_;
                    t();
                    --active_;
                    done_.notify_all();
                }
            });
        }
    }
    ~Pool() {
        { std::lock_guard<std::mutex> lk(m_); stop_ = true; }
        cv_.notify_all();
        for (auto& w : workers_) w.join();
    }
    template<typename F>
    void submit(F&& f) {
        { std::lock_guard<std::mutex> lk(m_); q_.emplace(std::forward<F>(f)); }
        cv_.notify_one();
    }
    void wait() {
        std::unique_lock<std::mutex> lk(m_);
        done_.wait(lk, [&] { return q_.empty() && active_ == 0; });
    }
};

// ── resume state ─────────────────────────────────────────────────────────────
struct State {
    std::set<std::string>                    visited;
    std::vector<std::pair<std::string, int>> queue;
    std::set<std::string>                    assets;
    std::vector<std::string>                 products;

    static State load(const std::string& path) {
        State s;
        std::string text;
        if (!read_text(path, text)) return s;
        std::stringstream ss(text);
        std::string line;
        while (std::getline(ss, line)) {
            auto tab = line.find('\t');
            if (tab == std::string::npos) continue;
            std::string kind = line.substr(0, tab);
            std::string val  = line.substr(tab + 1);
            if (kind == "V") s.visited.insert(val);
            else if (kind == "A") s.assets.insert(val);
            else if (kind == "P") s.products.push_back(val);
            else if (kind == "Q") {
                auto tab2 = val.find('\t');
                int d = 0;
                if (tab2 != std::string::npos) {
                    try { d = std::stoi(val.substr(tab2 + 1)); } catch (...) {}
                    val.resize(tab2);
                }
                s.queue.push_back({ val, d });
            }
        }
        return s;
    }

    void save(const std::string& path) const {
        make_parent(path);
        std::ofstream f(to_w(path).c_str(), std::ios::trunc);
        if (!f) return;
        for (auto& v : visited)  f << "V\t" << v << "\n";
        for (auto& a : assets)   f << "A\t" << a << "\n";
        for (auto& p : products) f << "P\t" << p << "\n";
        for (auto& q : queue)    f << "Q\t" << q.first << "\t" << q.second << "\n";
    }
};

// ── options ─────────────────────────────────────────────────────────────────
struct Options {
    std::string start_url;
    std::string output    = "grabbed";
    int  depth            = 6;
    int  threads          = 16;
    bool exhaust_pag      = true;
    bool extract_products = true;
    bool resume           = true;
    int  max_pages        = 200000;
};

// ── crawler ─────────────────────────────────────────────────────────────────
class Crawler {
public:
    void run(const Options& opts) {
        opts_ = opts;
        base_ = parse_url(opts_.start_url).host;

        auto t0 = std::chrono::steady_clock::now();

        std::cout << "\n[sitegrab v3/cpp] target   -> " << opts_.start_url << "\n"
                  << "[sitegrab v3/cpp] depth    -> " << opts_.depth << "\n"
                  << "[sitegrab v3/cpp] threads  -> " << opts_.threads << "\n"
                  << "[sitegrab v3/cpp] paginate -> " << (opts_.exhaust_pag ? "exhaust" : "off") << "\n"
                  << "[sitegrab v3/cpp] products -> " << (opts_.extract_products ? "yes" : "no") << "\n"
                  << "[sitegrab v3/cpp] output   -> "
                  << opts_.output << "/" << base_ << "/\n\n";

        std::string state_path = opts_.output + "/" + base_ + "/.sitegrab_state.tsv";
        State st;
        if (opts_.resume) st = State::load(state_path);

        std::deque<std::pair<std::string, int>> page_queue;
        if (!st.queue.empty()) {
            std::cout << "[resume] " << st.queue.size() << " pages from prior run\n";
            for (auto& x : st.queue) page_queue.push_back(x);
        } else {
            page_queue.push_back({ opts_.start_url, 0 });
            for (auto& u : fetch_sitemaps()) {
                if (same_domain(u, base_)) page_queue.push_back({ u, 1 });
            }
        }

        visited_  = st.visited;
        assets_   = st.assets;
        products_ = st.products;

        Pool pool(opts_.threads);
        int pages_done = 0;

        while (!page_queue.empty() && pages_done < opts_.max_pages) {
            std::vector<std::pair<std::string, int>> batch;
            while (!page_queue.empty() && (int)batch.size() < opts_.threads * 2) {
                auto x = page_queue.front();
                page_queue.pop_front();
                if (visited_.count(x.first)) continue;
                visited_.insert(x.first);
                batch.push_back(x);
            }
            if (batch.empty()) break;

            std::mutex proc_m;
            std::vector<std::string>                  new_assets;
            std::vector<std::string>                  new_products;
            std::vector<std::pair<std::string, int>>  new_child;
            std::atomic<int>                          this_batch_done{ 0 };

            for (auto& item : batch) {
                std::string url   = item.first;
                int         depth = item.second;

                pool.submit([&, url, depth] {
                    auto local = url_to_local(url, opts_.output);
                    if (local.empty()) { ++this_batch_done; return; }

                    auto r = http_get(url);
                    if (r.status != 200 || r.body.empty()) {
                        std::lock_guard<std::mutex> lk(proc_m);
                        std::cerr << "  [ERR " << r.status << "] " << url << "\n";
                        ++this_batch_done;
                        return;
                    }
                    write_binary(local, r.body);

                    {
                        std::lock_guard<std::mutex> lk(proc_m);
                        ++pages_done;
                        std::cout << "  [" << pages_done << "] page " << url << "\n";
                    }

                    std::string html(r.body.begin(), r.body.end());

                    auto assets = extract_assets(html, url);
                    auto links  = extract_links(html, url);
                    std::vector<std::string> products;
                    if (opts_.extract_products)
                        products = extract_products(html, url);

                    if (opts_.exhaust_pag) {
                        std::vector<std::string> extra_links;
                        for (auto& l : links) {
                            auto ex = expand_pagination(l);
                            for (auto& e : ex) extra_links.push_back(e);
                        }
                        for (auto& e : extra_links) links.push_back(e);
                    }

                    std::lock_guard<std::mutex> lk(proc_m);
                    for (auto& a : assets)   new_assets.push_back(a);
                    for (auto& p : products) new_products.push_back(p);
                    if (depth < opts_.depth)
                        for (auto& l : links)
                            new_child.push_back({ l, depth + 1 });

                    ++this_batch_done;
                });
            }

            while (this_batch_done.load() < (int)batch.size())
                std::this_thread::sleep_for(20ms);

            std::vector<std::string> queued;
            {
                std::lock_guard<std::mutex> lk(proc_m);
                for (auto& a : new_assets) {
                    if (assets_.insert(a).second) queued.push_back(a);
                }
                for (auto& p : new_products) products_.push_back(p);
                for (auto& c : new_child) {
                    if (!visited_.count(c.first)) page_queue.push_back(c);
                }
            }

            std::atomic<int> assets_done{ 0 };
            for (auto& a : queued) {
                pool.submit([&, a] {
                    auto local = url_to_local(a, opts_.output);
                    if (local.empty() || file_exists(local)) { ++assets_done; return; }
                    auto r = http_get(a);
                    if (r.status == 200 && !r.body.empty()) {
                        write_binary(local, r.body);
                        std::lock_guard<std::mutex> lk(proc_m);
                        std::cout << "       asset " << a << "\n";
                    }
                    ++assets_done;
                });
            }
            while (assets_done.load() < (int)queued.size())
                std::this_thread::sleep_for(20ms);

            {
                State snap;
                snap.visited  = visited_;
                snap.assets   = assets_;
                snap.products = products_;
                for (auto& q : page_queue) snap.queue.push_back(q);
                snap.save(state_path);
            }
        }

        pool.wait();
        css_sweep();

        if (!products_.empty()) {
            std::string mpath = opts_.output + "/" + base_ + "/_products.ndjson";
            make_parent(mpath);
            std::ofstream pf(to_w(mpath).c_str(), std::ios::trunc);
            for (auto& p : products_) pf << p << "\n";
            std::cout << "\n[sitegrab v3/cpp] products -> " << mpath << "\n";
        }

        auto sec = std::chrono::duration_cast<std::chrono::seconds>(
            std::chrono::steady_clock::now() - t0).count();

        std::cout << "\n" << std::string(60, '-') << "\n"
                  << "[sitegrab v3/cpp] pages    : " << pages_done << "\n"
                  << "[sitegrab v3/cpp] assets   : " << assets_.size() << "\n"
                  << "[sitegrab v3/cpp] products : " << products_.size() << "\n"
                  << "[sitegrab v3/cpp] elapsed  : " << sec << "s\n"
                  << "[sitegrab v3/cpp] output   : "
                  << opts_.output << "/" << base_ << "/\n"
                  << std::string(60, '-') << "\n";
    }

private:
    Options                  opts_;
    std::string              base_;
    std::set<std::string>    visited_;
    std::set<std::string>    assets_;
    std::vector<std::string> products_;

    std::vector<std::string> extract_assets(const std::string& html, const std::string& page) {
        std::vector<std::string> out;
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_SRC_HREF);
             it != std::sregex_iterator(); ++it) {
            auto raw = (*it)[1].str();
            if (sw(raw, "data:")) continue;
            auto u = join_url(page, raw);
            if (!u.empty()) out.push_back(u);
        }
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_SRCSET);
             it != std::sregex_iterator(); ++it) {
            std::string set = (*it)[1].str();
            std::stringstream ss(set);
            std::string part;
            while (std::getline(ss, part, ',')) {
                auto s = trim(part);
                auto sp = s.find(' ');
                if (sp != std::string::npos) s.resize(sp);
                auto u = join_url(page, s);
                if (!u.empty()) out.push_back(u);
            }
        }
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_CSS_URL);
             it != std::sregex_iterator(); ++it) {
            auto u = join_url(page, (*it)[1].str());
            if (!u.empty()) out.push_back(u);
        }
        return out;
    }

    std::vector<std::string> extract_links(const std::string& html, const std::string& page) {
        std::vector<std::string> out;
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_A_HREF);
             it != std::sregex_iterator(); ++it) {
            auto u = join_url(page, (*it)[1].str());
            if (!u.empty() && same_domain(u, base_)) out.push_back(u);
        }
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_FORM_ACTION);
             it != std::sregex_iterator(); ++it) {
            auto u = join_url(page, (*it)[1].str());
            if (!u.empty() && same_domain(u, base_)) out.push_back(u);
        }
        return out;
    }

    std::vector<std::string> extract_products(const std::string& html, const std::string& page) {
        std::vector<std::string> out;
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_JSONLD);
             it != std::sregex_iterator(); ++it) {
            std::string blob = (*it)[1].str();
            if (blob.find("\"Product\"")  != std::string::npos ||
                blob.find("\"Offer\"")    != std::string::npos ||
                blob.find("\"ItemPage\"") != std::string::npos) {
                out.push_back("{\"kind\":\"json-ld\",\"url\":\"" + page +
                              "\",\"data\":" + blob + "}");
            }
        }
        for (auto it = std::sregex_iterator(html.begin(), html.end(), RE_OG_META);
             it != std::sregex_iterator(); ++it) {
            std::string prop = (*it)[1].str();
            if (sw(prop, "product:") || prop == "og:price:amount") {
                out.push_back("{\"kind\":\"og\",\"url\":\"" + page +
                              "\",\"property\":\"" + prop +
                              "\",\"value\":\"" + (*it)[2].str() + "\"}");
            }
        }
        return out;
    }

    std::vector<std::string> fetch_sitemaps() {
        std::vector<std::string> out;
        auto p = parse_url(opts_.start_url);

        const char* paths[] = {
            "/sitemap.xml", "/sitemap_index.xml", "/sitemap.txt",
            "/sitemap-products.xml", "/product-sitemap.xml"
        };

        for (auto path : paths) {
            std::string url = p.scheme + "://" + p.host + path;
            auto r = http_get(url);
            if (r.status != 200 || r.body.empty()) continue;

            std::string body(r.body.begin(), r.body.end());
            size_t before = out.size();

            if (ew(std::string(path), ".txt")) {
                std::stringstream ss(body);
                std::string line;
                while (std::getline(ss, line)) {
                    line = trim(line);
                    if (sw(line, "http")) out.push_back(line);
                }
            } else {
                for (auto it = std::sregex_iterator(body.begin(), body.end(), RE_LOC);
                     it != std::sregex_iterator(); ++it) {
                    out.push_back((*it)[1].str());
                }
            }

            std::cout << "  [sitemap] " << path
                      << " -> " << (out.size() - before) << " urls\n";
        }
        return out;
    }

    void css_sweep() {
        std::vector<std::string> css_files;
        std::string base_dir = opts_.output + "/" + base_;
        walk_css(base_dir, css_files);

        if (css_files.empty()) return;

        std::vector<std::string> wave2;
        for (auto& css_path : css_files) {
            std::string text;
            if (!read_text(css_path, text)) continue;
            for (auto it = std::sregex_iterator(text.begin(), text.end(), RE_CSS_URL);
                 it != std::sregex_iterator(); ++it) {
                auto u = join_url("https://" + base_ + "/", (*it)[1].str());
                if (!u.empty() && assets_.insert(u).second)
                    wave2.push_back(u);
            }
        }

        if (wave2.empty()) return;
        std::cout << "\n[css sweep] " << wave2.size() << " additional assets\n";

        Pool p2(opts_.threads);
        std::atomic<int> done{ 0 };
        for (auto& a : wave2) {
            p2.submit([&, a] {
                auto local = url_to_local(a, opts_.output);
                if (!local.empty() && !file_exists(local)) {
                    auto r = http_get(a);
                    if (r.status == 200 && !r.body.empty()) {
                        write_binary(local, r.body);
                        std::cout << "       css-asset " << a << "\n";
                    }
                }
                ++done;
            });
        }
        while (done.load() < (int)wave2.size())
            std::this_thread::sleep_for(20ms);
        p2.wait();
    }
};

// ── main ─────────────────────────────────────────────────────────────────────
int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cout << "SiteGrabber v3 (C++, WinHTTP)\n\n"
                  << "usage: sitegrab <url> [output] [depth] [threads] "
                     "[--no-paginate] [--no-products] [--no-resume]\n";
        return 1;
    }
    if (!http_init()) {
        std::cerr << "[fatal] WinHttpOpen failed (" << GetLastError() << ")\n";
        return 1;
    }

    Options opts;
    opts.start_url = argv[1];
    int pos = 2;
    if (pos < argc && argv[pos][0] != '-') opts.output  = argv[pos++];
    if (pos < argc && argv[pos][0] != '-') opts.depth   = std::stoi(argv[pos++]);
    if (pos < argc && argv[pos][0] != '-') opts.threads = std::stoi(argv[pos++]);

    for (int i = pos; i < argc; ++i) {
        std::string a = argv[i];
        if      (a == "--no-paginate") opts.exhaust_pag = false;
        else if (a == "--no-products") opts.extract_products = false;
        else if (a == "--no-resume")   opts.resume = false;
    }

    Crawler c;
    c.run(opts);
    http_cleanup();
    return 0;
}