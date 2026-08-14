//! Native Windows Toast Notification builder.

pub fn generate_toast_xml(title: &str, message: &str, action_tag: &str) -> String {
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
}
