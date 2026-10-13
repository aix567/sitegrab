// language: Rust, file: src/main.rs
// SiteGrabber v3.0 — Concurrent subdomain enumerator
// Output (stdout): "sub.example.com 1.2.3.4" per resolved subdomain
// Usage: subdomain_enum <root_domain> [--threads N] [--wordlist PATH]

use clap::Parser;
use futures::stream::{self, StreamExt};
use hickory_resolver::{
    config::{ResolverConfig, ResolverOpts},
    TokioAsyncResolver,
};
use reqwest::Client;
use serde::Deserialize;
use std::{
    collections::HashSet,
    path::PathBuf,
    sync::{Arc, Mutex},
};

// ── CLI ───────────────────────────────────────────────────────────────────────
#[derive(Parser)]
#[command(name = "subdomain_enum", about = "SiteGrabber v3.0 subdomain enumerator")]
struct Cli {
    /// Root domain to enumerate (e.g. example.com)
    domain: String,
    /// Concurrent DNS threads
    #[arg(short, long, default_value_t = 100)]
    threads: usize,
    /// Custom wordlist path (one word per line)
    #[arg(short, long)]
    wordlist: Option<PathBuf>,
}

// ── Built-in wordlist ─────────────────────────────────────────────────────────
const BUILTIN_WORDS: &[&str] = &[
    "www","mail","api","dev","staging","test","blog","shop","app","cdn",
    "admin","auth","login","portal","dashboard","static","assets","media",
    "upload","download","docs","support","help","forum","ftp","smtp","vpn",
    "remote","git","gitlab","github","jira","jenkins","ci","build","beta",
    "alpha","preview","sandbox","demo","old","new","secure","ssl","ns1","ns2",
    "mx","pop","imap","autodiscover","webmail","cpanel","whm","direct","mobile",
    "m","api2","api3","v1","v2","v3","internal","intranet","monitor","status",
    "health","metrics","grafana","prometheus","elastic","kibana","redis","mysql",
    "db","database","backup","archive","logs","files","images","img","js","css",
    "assets2","s3","storage","cloud","email","smtp2","relay","proxy","gateway",
    "vpn2","employee","hr","finance","accounting","crm","erp","wiki","confluence",
    "stackoverflow","answers","community","partners","affiliate","careers",
    "recruitment","events","press","news","media2","store","cart","checkout",
    "payment","billing","invoice","subscription","service","services","prod",
    "production","staging2","preprod","qa","uat","integration","ops","devops",
    "sre","security","audit","compliance","legal","privacy","gdpr",
];

// ── crt.sh response ───────────────────────────────────────────────────────────
#[derive(Deserialize)]
struct CrtEntry {
    name_value: String,
}

// ── Entry ─────────────────────────────────────────────────────────────────────
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    let root = cli.domain.trim_start_matches("*.").to_lowercase();

    // Shared resolver + dedup set
    let mut opts = ResolverOpts::default();
    opts.timeout  = std::time::Duration::from_secs(3);
    opts.attempts = 2;
    let resolver = Arc::new(
        TokioAsyncResolver::tokio(ResolverConfig::default(), opts)
    );
    let seen: Arc<Mutex<HashSet<String>>> = Arc::new(Mutex::new(HashSet::new()));

    // ── 1. Passive: crt.sh ───────────────────────────────────────────────────
    let passive = fetch_crtsh(&root).await;
    let mut candidates: Vec<String> = passive.into_iter()
        .filter(|s| s.ends_with(&format!(".{root}")))
        .map(|s| s.trim_start_matches("*.").to_string())
        .collect();

    // ── 2. Active: wordlist bruteforce ────────────────────────────────────────
    let words: Vec<String> = if let Some(path) = &cli.wordlist {
        std::fs::read_to_string(path)?
            .lines()
            .map(|l| format!("{}.{root}", l.trim()))
            .collect()
    } else {
        BUILTIN_WORDS.iter().map(|w| format!("{w}.{root}")).collect()
    };
    candidates.extend(words);

    // Deduplicate
    candidates.sort_unstable();
    candidates.dedup();

    // ── 3. Concurrent DNS resolution ─────────────────────────────────────────
    let resolver_ref = &resolver;
    let seen_ref     = &seen;
    let root_ref     = &root;

    stream::iter(candidates)
        .for_each_concurrent(cli.threads, |sub| async move {
            // Skip if already seen
            {
                let mut s = seen_ref.lock().unwrap();
                if !s.insert(sub.clone()) { return; }
            }
            if !sub.ends_with(root_ref.as_str()) { return; }

            match resolver_ref.lookup_ip(sub.as_str()).await {
                Ok(resp) => {
                    let ip = resp.iter().next()
                        .map(|a| a.to_string())
                        .unwrap_or_default();
                    // Output one line per resolved subdomain
                    println!("{sub} {ip}");
                }
                Err(_) => {}  // NXDOMAIN / timeout — silent
            }
        })
        .await;

    Ok(())
}

// ── crt.sh query ─────────────────────────────────────────────────────────────
async fn fetch_crtsh(domain: &str) -> Vec<String> {
    let url = format!("https://crt.sh/?q=%.{domain}&output=json");
    let client = Client::builder()
        .timeout(std::time::Duration::from_secs(10))
        .danger_accept_invalid_certs(false)
        .build();

    let Ok(client) = client else { return vec![] };

    match client.get(&url).send().await {
        Ok(resp) => {
            match resp.json::<Vec<CrtEntry>>().await {
                Ok(entries) => entries.into_iter().map(|e| e.name_value).collect(),
                Err(_) => vec![],
            }
        }
        Err(e) => {
            eprintln!("[WARN] crt.sh failed: {e}");
            vec![]
        }
    }
}
