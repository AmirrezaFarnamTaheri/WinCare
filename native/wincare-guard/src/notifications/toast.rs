//! Native Windows Toast notification XML builder.

/// Builds a Windows toast notification XML document for a guard alert.
///
/// The title, message, and action tag are escaped for XML and URI contexts so untrusted
/// monitor-derived values cannot break out of the document. The resulting XML is a
/// `ToastGeneric` template with a protocol-activation action and a system dismiss action.
pub fn generate_toast_xml(title: &str, message: &str, action_tag: &str) -> String {
    let title = escape_xml(title);
    let message = escape_xml(message);
    let action_tag = encode_uri_segment(action_tag);
    format!(
        r#"<toast launch="wincare://action/{action_tag}">
    <visual>
        <binding template="ToastGeneric">
            <text>{title}</text>
            <text>{message}</text>
        </binding>
    </visual>
    <actions>
        <action content="Open WinCare" arguments="wincare://open/{action_tag}" activationType="protocol"/>
        <action content="Dismiss" arguments="dismiss" activationType="system"/>
    </actions>
</toast>"#
    )
}

fn escape_xml(value: &str) -> String {
    value
        .chars()
        .filter(|&character| is_valid_xml_char(character))
        .collect::<String>()
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

/// Returns true for XML 1.0 valid characters (tab, LF, CR, and the allowed Unicode ranges).
fn is_valid_xml_char(character: char) -> bool {
    matches!(
        character,
        '\u{9}'
            | '\u{A}'
            | '\u{D}'
            | '\u{20}'..='\u{D7FF}'
            | '\u{E000}'..='\u{FFFD}'
            | '\u{10000}'..='\u{10FFFF}'
    )
}

fn encode_uri_segment(value: &str) -> String {
    let mut encoded = String::with_capacity(value.len());
    for byte in value.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~') {
            encoded.push(char::from(byte));
        } else {
            use std::fmt::Write;
            write!(encoded, "%{byte:02X}").expect("writing to a String cannot fail");
        }
    }
    encoded
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_toast_xml_generation() {
        let xml = generate_toast_xml(
            "Storage Alert",
            "Drive C has less than 5 GB free",
            "clean_temp",
        );
        assert!(xml.contains("Storage Alert"));
        assert!(xml.contains("Drive C has less than 5 GB free"));
        assert!(xml.contains("wincare://open/clean_temp"));
    }

    #[test]
    fn escapes_untrusted_xml_and_uri_values() {
        let xml = generate_toast_xml(
            "Storage <alert> & warning",
            "Free space: \"low\"",
            "clean/temp?force=true&source=guard",
        );

        assert!(xml.contains("Storage &lt;alert&gt; &amp; warning"));
        assert!(xml.contains("Free space: &quot;low&quot;"));
        assert!(xml.contains("clean%2Ftemp%3Fforce%3Dtrue%26source%3Dguard"));
        assert!(!xml.contains("clean/temp?force=true"));
    }

    #[test]
    fn removes_xml_invalid_control_characters() {
        let xml = generate_toast_xml("title\0\u{1}\u{8}\u{B}\u{C}\u{B}ok", "message", "tag");
        assert!(xml.contains("titleok"));
        assert!(!xml.contains('\0'));
        assert!(!xml.contains('\u{1}'));
        assert!(!xml.contains('\u{8}'));
        assert!(!xml.contains('\u{B}'));
        assert!(!xml.contains('\u{C}'));
    }
}
